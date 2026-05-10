using System.Net.Http;

namespace System
{
    internal static class Net48StringExtensions
    {
        public static bool Contains(this string source, string value, StringComparison comparisonType)
        {
            return source.IndexOf(value, comparisonType) >= 0;
        }
    }
}

namespace System.Collections.Generic
{
    internal static class Net48DictionaryExtensions
    {
        public static bool Remove<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (!dictionary.TryGetValue(key, out value!))
            {
                return false;
            }

            return dictionary.Remove(key);
        }

        public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        {
            return dictionary.TryGetValue(key, out var value) ? value : default;
        }
    }
}

namespace System.Linq
{
    internal static class Net48EnumerableExtensions
    {
        public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> source, int count)
        {
            return source.Skip(Math.Max(0, source.Count() - count));
        }

        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source, int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            var chunk = new List<T>(size);
            foreach (var item in source)
            {
                chunk.Add(item);
                if (chunk.Count == size)
                {
                    yield return chunk.ToArray();
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
            {
                yield return chunk.ToArray();
            }
        }
    }
}

namespace System.IO
{
    internal static class Net48StreamExtensions
    {
        public static Task WriteAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return stream.WriteAsync(buffer, 0, buffer.Length);
        }
    }
}

namespace System.Net.Http
{
    internal static class Net48HttpContentExtensions
    {
        public static Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return content.ReadAsStringAsync();
        }

        public static Task<Stream> ReadAsStreamAsync(this HttpContent content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return content.ReadAsStreamAsync();
        }
    }
}

namespace System.Threading
{
    internal static class Net48CancellationTokenSourceExtensions
    {
        public static Task CancelAsync(this CancellationTokenSource source)
        {
            source.Cancel();
            return Task.CompletedTask;
        }
    }
}
