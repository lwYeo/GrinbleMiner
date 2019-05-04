using System.Collections.Concurrent;

namespace GrinbleMiner.POW.Crypto
{
    public static class Blake2B_Helper
    {

        private static readonly ConcurrentBag<Blake2B> blake2Bs =
            new ConcurrentBag<Blake2B>();

        public static Blake2B GetInstance()
        {
            if (blake2Bs.TryTake(out var blake2B)) return blake2B;
            return new Blake2B(256);
        }

        public static void ReturnInstance(Blake2B blake2B)
        {
            if (blake2Bs.Count < 64)
            {
                blake2Bs.Add(blake2B);
                blake2B.HashClear();
            }
        }

    }
}
