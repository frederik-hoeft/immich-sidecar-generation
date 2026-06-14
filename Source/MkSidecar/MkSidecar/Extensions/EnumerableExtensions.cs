using System;
using System.Collections.Generic;
using System.Text;

namespace MkSidecar.Extensions;

internal static class EnumerableExtensions
{
    extension<T> (IEnumerable<T> self)
    {
        public TAccumulate? Aggregate<TAccumulate>(Func<T, TAccumulate> seed, Func<TAccumulate, T, TAccumulate> func)
        {
            TAccumulate? accumulate = default;
            foreach (T item in self)
            {
                accumulate = seed(item);
                break;
            }
            foreach (T item in self.Skip(1))
            {
                accumulate = func(accumulate!, item);
            }
            return accumulate;
        }
    }
}
