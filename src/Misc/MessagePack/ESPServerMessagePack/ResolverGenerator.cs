using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace eft_dma_radar.Common.Misc.MessagePack.ESPServerMessagePack
{
    public static class ResolverGenerator
    {
        public static readonly IFormatterResolver Instance;

        static ResolverGenerator()
        {
            Instance = CompositeResolver.Create(
                new IMessagePackFormatter[]
                {
                    new Vector2Formatter(),
                    new Vector3Formatter(),
                    new Vector3DictionaryFormatter() // ✅ Add the dictionary formatter
                },
                new IFormatterResolver[]
                {
                    StandardResolver.Instance
                }
            );
        }
    }
}
