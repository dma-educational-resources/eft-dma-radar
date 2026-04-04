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

namespace VmmSharpEx;

/// <summary>
/// Thrown when an exception occurs within Vmmsharp (MemProcFS).
/// </summary>
public sealed class VmmException : Exception
{
    public VmmException() { }

    public VmmException(string message)
        : base(message) { }

    public VmmException(string message, Exception inner)
        : base(message, inner) { }
}