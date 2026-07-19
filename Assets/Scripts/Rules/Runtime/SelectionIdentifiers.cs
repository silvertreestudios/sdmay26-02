using System;

namespace Game.Rules.Runtime
{
    /// <summary>Identifies a stable, presentation-independent selection prompt.</summary>
    public readonly struct SelectionRequestId : IEquatable<SelectionRequestId>
    {
        private readonly string value;

        /// <summary>Gets the stable identifier, or an empty string for the default value.</summary>
        public string Value => value ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => Value.Length == 0;

        /// <summary>Creates a request identifier.</summary>
        /// <param name="value">The non-empty stable identifier.</param>
        public SelectionRequestId(string value) =>
            this.value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(SelectionRequestId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SelectionRequestId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;
    }

    /// <summary>Identifies a data-defined burst, cone, emanation, line, or other area template.</summary>
    public readonly struct AreaTemplateId : IEquatable<AreaTemplateId>
    {
        private readonly string value;

        /// <summary>Gets the stable identifier, or an empty string for the default value.</summary>
        public string Value => value ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => Value.Length == 0;

        /// <summary>Creates an area-template identifier.</summary>
        /// <param name="value">The non-empty stable identifier.</param>
        public AreaTemplateId(string value) => this.value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(AreaTemplateId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AreaTemplateId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;
    }

    /// <summary>Identifies one rules-defined variant of a spell.</summary>
    public readonly struct SpellVariantId : IEquatable<SpellVariantId>
    {
        private readonly string value;

        /// <summary>Gets the stable identifier, or an empty string for the default value.</summary>
        public string Value => value ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => Value.Length == 0;

        /// <summary>Creates a spell-variant identifier.</summary>
        /// <param name="value">The non-empty stable identifier.</param>
        public SpellVariantId(string value) => this.value = StableId.Require(value, nameof(value));

        /// <inheritdoc/>
        public bool Equals(SpellVariantId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SpellVariantId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        /// <inheritdoc/>
        public override string ToString() => Value;
    }

    /// <summary>Identifies an open, data-defined movement mode such as land, fly, or swim.</summary>
    public readonly struct MovementMode : IEquatable<MovementMode>
    {
        private readonly string slug;

        /// <summary>Gets the stable movement-mode slug, or an empty string by default.</summary>
        public string Slug => slug ?? string.Empty;

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => Slug.Length == 0;

        /// <summary>Creates a movement mode from a stable data slug.</summary>
        /// <param name="slug">The non-empty mode slug.</param>
        public MovementMode(string slug) => this.slug = StableId.Require(slug, nameof(slug));

        /// <inheritdoc/>
        public bool Equals(MovementMode other) =>
            string.Equals(Slug, other.Slug, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MovementMode other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Slug);

        /// <inheritdoc/>
        public override string ToString() => Slug;
    }
}
