using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// An open, data-backed PF2e trait slug. New data does not require an enum change.
    /// </summary>
    public readonly struct Trait : IEquatable<Trait>
    {
        public string Slug { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private Trait(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        public static Trait FromName(string value) => FromSlug(Pf2eSlug.FromName(value));
        public static Trait FromSlug(string value) => new Trait(Pf2eSlug.FromName(value));
        public bool Equals(Trait other) => string.Equals(Slug, other.Slug, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is Trait other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);
        public override string ToString() => Slug ?? string.Empty;
        public static bool operator ==(Trait left, Trait right) => left.Equals(right);
        public static bool operator !=(Trait left, Trait right) => !left.Equals(right);
    }

    /// <summary>
    /// Identifies a rule's data source without a closed source-kind enum.
    /// </summary>
    public readonly struct RuleSource : IEquatable<RuleSource>
    {
        public string Slug { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private RuleSource(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        public static RuleSource FromName(string value) => FromSlug(Pf2eSlug.FromName(value));
        public static RuleSource FromSlug(string value) => new RuleSource(Pf2eSlug.FromName(value));
        public bool Equals(RuleSource other) => string.Equals(Slug, other.Slug, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RuleSource other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);
        public override string ToString() => Slug ?? string.Empty;
        public static bool operator ==(RuleSource left, RuleSource right) => left.Equals(right);
        public static bool operator !=(RuleSource left, RuleSource right) => !left.Equals(right);
    }

    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public GridPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(GridPosition other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
        public static bool operator ==(GridPosition left, GridPosition right) => left.Equals(right);
        public static bool operator !=(GridPosition left, GridPosition right) => !left.Equals(right);
    }

    public readonly struct GridDistance : IEquatable<GridDistance>, IComparable<GridDistance>
    {
        public int Feet { get; }

        public GridDistance(int feet)
        {
            if (feet < 0)
                throw new ArgumentOutOfRangeException(nameof(feet));
            Feet = feet;
        }

        public int CompareTo(GridDistance other) => Feet.CompareTo(other.Feet);
        public bool Equals(GridDistance other) => Feet == other.Feet;
        public override bool Equals(object obj) => obj is GridDistance other && Equals(other);
        public override int GetHashCode() => Feet;
        public override string ToString() => $"{Feet} feet";
        public static bool operator ==(GridDistance left, GridDistance right) => left.Equals(right);
        public static bool operator !=(GridDistance left, GridDistance right) => !left.Equals(right);
    }
}
