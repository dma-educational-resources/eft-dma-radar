/*  
*  C# API wrapper 'vmmsharp' for MemProcFS 'vmm.dll' and LeechCore 'leechcore.dll' APIs.
*  
*  Please see the example project in vmmsharp_example for additional information.
*  
*  Please consult the C/C++ header files vmmdll.h and leechcore.h for information about parameters and API usage.
*  
*  (c) Ulf Frisk, 2020-2025
*  Author: Ulf Frisk, pcileech@frizk.net
*  
*/

/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VmmSharpEx.Internal;
using VmmSharpEx.Options;

namespace VmmSharpEx;

/// <summary>
/// VmmSearch represents a binary search in memory.
/// </summary>
public static class VmmSearch
{
    private const uint VMMDLL_MEM_SEARCH_VERSION = 0xfe3e0003;
    private const int VMMDLL_MEM_SEARCH_MAXLENGTH = 32;
    private static readonly ConcurrentDictionary<IntPtr, SearchResult> _contexts = new();

    /// <summary>
    /// Asynchronously execute a memory search on the specified process.
    /// </summary>
    /// <param name="vmm">Vmm instance</param>
    /// <param name="pid">Process ID</param>
    /// <param name="searchItems">Search items</param>
    /// <param name="addr_min">(Optional) Minimum address</param>
    /// <param name="addr_max">(Optional) Maximum address</param>
    /// <param name="cMaxResult">(Optional) Maximum results</param>
    /// <param name="readFlags">(Optional) Vmm Read flags</param>
    /// <param name="ct">(Optional) Cancellation token to abort the search.</param>
    /// <returns><see cref="SearchResult"/> object containing the search result(s).</returns>
    public static Task<SearchResult> MemSearchAsync(
        this Vmm vmm,
        uint pid,
        IEnumerable<SearchItem> searchItems,
        ulong addr_min = 0,
        ulong addr_max = ulong.MaxValue,
        uint cMaxResult = 0,
        VmmFlags readFlags = VmmFlags.NONE,
        CancellationToken ct = default)
    {
        return Task.Run(() => MemSearch(
            vmm: vmm,
            pid: pid,
            searchItems: searchItems,
            addr_min: addr_min,
            addr_max: addr_max,
            cMaxResult: cMaxResult,
            readFlags: readFlags,
            ct: ct));
    }

    /// <summary>
    /// Execute a memory search on the specified process.
    /// </summary>
    /// <param name="vmm">Vmm instance</param>
    /// <param name="pid">Process ID</param>
    /// <param name="searchItems">Search items</param>
    /// <param name="addr_min">(Optional) Minimum address</param>
    /// <param name="addr_max">(Optional) Maximum address</param>
    /// <param name="cMaxResult">(Optional) Maximum results</param>
    /// <param name="readFlags">(Optional) Vmm Read flags</param>
    /// <param name="ct">(Optional) Cancellation token to abort the search.</param>
    /// <returns><see cref="SearchResult"/> object containing the search result(s).</returns>
    public static unsafe SearchResult MemSearch(this Vmm vmm,
        uint pid,
        IEnumerable<SearchItem> searchItems,
        ulong addr_min = 0,
        ulong addr_max = ulong.MaxValue,
        uint cMaxResult = 0,
        VmmFlags readFlags = VmmFlags.NONE,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new SearchResult();
        var searches = ProcessSearchEntries(searchItems);
        if (searches.Length == 0)
        {
            return result; // No search items, return empty result.
        }
        var context = (Vmmi.VMMDLL_MEM_SEARCH_CONTEXT*)NativeMemory.Alloc((nuint)sizeof(Vmmi.VMMDLL_MEM_SEARCH_CONTEXT));
        try
        {
            fixed (void* pSearches = searches)
            {
                *context = new Vmmi.VMMDLL_MEM_SEARCH_CONTEXT
                {
                    dwVersion = VMMDLL_MEM_SEARCH_VERSION,
                    vaMin = addr_min,
                    vaMax = addr_max,
                    cMaxResult = cMaxResult,
                    ReadFlags = (uint)readFlags,
                    pvUserPtrOpt = (IntPtr)context,
                    pfnResultOptCB = &SearchResultCallback,
                    cSearch = (uint)searches.Length,
                    search = pSearches
                };
                _contexts.TryAdd((IntPtr)context, result);
                var ctReg = ct.Register(() =>
                {
                    context->fAbortRequested = 1;
                });
                try
                {
                    result.IsSuccess = Vmmi.VMMDLL_MemSearch(vmm, pid, context, IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    // IMPORTANT: Ensure we unregister the cancellation token callback before the context is freed.
                    // This will block (WaitForCallbackIfNecessary) until the callback is completed (if it is already in progress).
                    ctReg.Dispose();
                }

                ct.ThrowIfCancellationRequested();
                return result;
            }
        }
        finally
        {
            NativeMemory.Free(context);
            _contexts.TryRemove((IntPtr)context, out _);
        }
    }

    [UnmanagedCallersOnly]
    private static int SearchResultCallback(Vmmi.VMMDLL_MEM_SEARCH_CONTEXT ctx, ulong va, uint iSearch)
    {
        if (!_contexts.TryGetValue(ctx.pvUserPtrOpt, out var result))
            return 0;
        var e = new SearchResult.Entry
        {
            Address = va,
            SearchTermId = iSearch
        };
        result._results.Add(e);
        return result._results.Count < ctx.cMaxResult ?
            1 : 0;
    }

    private static unsafe Vmmi.VMMDLL_MEM_SEARCH_CONTEXT_SEARCHENTRY[] ProcessSearchEntries(IEnumerable<SearchItem>? items)
    {
        ArgumentNullException.ThrowIfNull(items, nameof(items));
        var searches = new List<Vmmi.VMMDLL_MEM_SEARCH_CONTEXT_SEARCHENTRY>();
        foreach (var item in items)
        {
            var search = item.Search.AsSpan();
            var skipmask = item.SkipMask is null ?
                default : item.SkipMask.AsSpan();
            ArgumentOutOfRangeException.ThrowIfGreaterThan(search.Length, VMMDLL_MEM_SEARCH_MAXLENGTH, nameof(item.Search));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(skipmask.Length, VMMDLL_MEM_SEARCH_MAXLENGTH, nameof(item.SkipMask));
            var e = new Vmmi.VMMDLL_MEM_SEARCH_CONTEXT_SEARCHENTRY
            {
                cbAlign = item.Align,
                cb = (uint)search.Length
            };
            var pbSearch = new Span<byte>(e.pb, VMMDLL_MEM_SEARCH_MAXLENGTH);
            search.Slice(0, Math.Min(search.Length, VMMDLL_MEM_SEARCH_MAXLENGTH)).CopyTo(pbSearch);
            if (!skipmask.IsEmpty && skipmask.Length > 0)
            {
                var pbSkipMask = new Span<byte>(e.pbSkipMask, VMMDLL_MEM_SEARCH_MAXLENGTH);
                skipmask.Slice(0, Math.Min(skipmask.Length, VMMDLL_MEM_SEARCH_MAXLENGTH)).CopyTo(pbSkipMask);
            }

            searches.Add(e);
        }
        return searches.ToArray();
    }

    /// <summary>
    /// Represents a single search item with search bytes, optional skip mask and alignment.
    /// </summary>
    public readonly struct SearchItem
    {
        public readonly byte[] Search { get; init; }
        public readonly byte[]? SkipMask { get; init; } = null;
        public readonly uint Align { get; init; } = 1;

        public SearchItem(byte[] search, byte[]? skipMask = null, uint align = 1)
        {
            Search = search;
            SkipMask = skipMask;
            Align = align;
        }
    }

    /// <summary>
    /// Struct with info about the current search results.
    /// </summary>
    public sealed class SearchResult
    {
        internal SearchResult() { }

        /// <summary>
        /// If <see cref="IsSuccess"/> is <see langword="true"/> this indicates that the search was completed successfully without errors. However, it does not guarantee that any results were found.
        /// Be sure to check <see cref="Results"/> for the number of results found.
        /// </summary>
        public bool IsSuccess { get; internal set; }

        internal readonly ConcurrentBag<Entry> _results = new();
        /// <summary>
        /// The search results.
        /// </summary>
        public IReadOnlyCollection<Entry> Results => _results;

        /// <summary>
        /// Struct with info about a single search result. Address, search term id.
        /// </summary>
        public readonly struct Entry
        {
            public readonly ulong Address { get; init; }
            public readonly ulong SearchTermId { get; init; }
        }
    }
}