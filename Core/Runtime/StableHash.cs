using System.Text;

namespace PFound.RemoteGameConfig.Core
{
    /// <summary>
    /// A fixed, cross-platform-stable, non-cryptographic hash (FNV-1a, 64-bit) over the UTF-8 bytes of
    /// a string. Used to bucket an install into an experiment variant deterministically. It must be a
    /// documented, run-stable algorithm — deliberately NOT <see cref="object.GetHashCode"/>, whose
    /// result is randomized per process on modern runtimes and would re-bucket an install every launch.
    /// The same input always yields the same 64-bit digest on every platform and every run.
    /// </summary>
    public static class StableHash
    {
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        public static ulong Of(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }
            return hash;
        }

        /// <summary>
        /// Map a string onto <c>[0, buckets)</c> deterministically. With <c>buckets == 100</c> this is
        /// the percentage bucket an experiment rollout range is compared against.
        /// </summary>
        public static int Bucket(string text, int buckets) => (int)(Of(text) % (ulong)buckets);
    }
}
