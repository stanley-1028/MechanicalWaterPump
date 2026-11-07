using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MWP.Core
{
    /// <summary>An immutable (good id, amount) pair. Amounts are whole items.</summary>
    public readonly struct GoodStack : IEquatable<GoodStack>
    {
        /// <summary>Creates a pair.</summary>
        /// <param name="goodId">Game good id, for example "Water", "Log", "Plank", "MetalBlock".</param>
        /// <param name="amount">Non-negative item count.</param>
        public GoodStack(string goodId, int amount)
        {
            GoodId = goodId ?? throw new ArgumentNullException(nameof(goodId));
            Amount = amount;
        }

        /// <summary>Game good id.</summary>
        public string GoodId { get; }

        /// <summary>Non-negative item count.</summary>
        public int Amount { get; }

        /// <inheritdoc />
        public bool Equals(GoodStack other) => string.Equals(GoodId, other.GoodId, StringComparison.Ordinal) && Amount == other.Amount;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GoodStack other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (StringComparer.Ordinal.GetHashCode(GoodId) * 397) ^ Amount;

        /// <inheritdoc />
        public override string ToString() => Amount + " " + GoodId;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(GoodStack left, GoodStack right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(GoodStack left, GoodStack right) => !left.Equals(right);
    }

    /// <summary>A good id plus its default maintenance requirement, all validated on construction.</summary>
    public static class GoodStacks
    {
        /// <summary>Water good id (vanilla good).</summary>
        public const string Water = "Water";

        /// <summary>Log good id (vanilla good).</summary>
        public const string Log = "Log";

        /// <summary>Plank good id (vanilla good).</summary>
        public const string Plank = "Plank";

        /// <summary>MetalBlock good id (vanilla good).</summary>
        public const string MetalBlock = "MetalBlock";

        /// <summary>Read-only helper that freezes an array of stacks.</summary>
        /// <param name="stacks">Stacks to freeze.</param>
        public static IReadOnlyList<GoodStack> Freeze(params GoodStack[] stacks)
        {
            if (stacks == null)
            {
                throw new ArgumentNullException(nameof(stacks));
            }

            return new ReadOnlyCollection<GoodStack>(stacks) as IReadOnlyList<GoodStack>;
        }
    }
}
