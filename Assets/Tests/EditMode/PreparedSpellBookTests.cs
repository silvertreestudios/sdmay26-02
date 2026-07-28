using System;
using System.Collections.Generic;
using Game.Combat.Spells;
using Game.Rules.Runtime;
using NUnit.Framework;

public sealed class PreparedSpellBookTests
{
    private static readonly CreatureId Owner = new("cleric");
    private static readonly SpellReference Light = new(new SpellId("light"), 1);
    private static readonly SpellReference Bless = new(new SpellId("bless"), 1);
    private static readonly SpellReference Heal = new(new SpellId("heal"), 1);
    private static readonly SpellSlotPoolId PreparedPool = new("prepared-rank-1");
    private static readonly SpellSlotPoolId FontPool = new("font-heal");

    [Test]
    public void ExactRankPreparationAndDeduplicationAreStable()
    {
        PreparedSpellBook book = CreateBook(
            PreparedSpellEntry.Cantrip(Light),
            PreparedSpellEntry.Cantrip(Light)
        );

        Assert.That(book.CastableSpells, Is.EqualTo(new[] { Light }));
        Assert.That(
            book.Authorize(Owner, Light, new Reader()).Kind,
            Is.EqualTo(SpellCastResourceKind.Cantrip)
        );
        Assert.That(
            book.Authorize(
                Owner,
                new SpellReference(new SpellId("light"), 2),
                new Reader()
            ).IsAuthorized,
            Is.False
        );
        Assert.That(
            book.Authorize(
                Owner,
                new SpellReference(new SpellId("unknown"), 1),
                new Reader()
            ).IsAuthorized,
            Is.False
        );
    }

    [Test]
    public void PreparedAndFontSpellsAuthorizeOnlyTheirExactPools()
    {
        PreparedSpellBook book = CreateBook(
            PreparedSpellEntry.FromPool(Bless, PreparedPool),
            PreparedSpellEntry.FromPool(Heal, FontPool)
        );
        Reader reader = new(
            new SpellSlotState(Scoped(PreparedPool), Owner, 1, 1),
            new SpellSlotState(Scoped(FontPool), Owner, 4, 4)
        );

        Assert.That(book.Authorize(Owner, Bless, reader).Pool, Is.EqualTo(Scoped(PreparedPool)));
        Assert.That(book.Authorize(Owner, Heal, reader).Pool, Is.EqualTo(Scoped(FontPool)));
    }

    [Test]
    public void MissingExhaustedAndWrongOwnerPoolsReject()
    {
        PreparedSpellBook book = CreateBook(PreparedSpellEntry.FromPool(Bless, PreparedPool));

        Assert.That(book.Authorize(Owner, Bless, new Reader()).IsAuthorized, Is.False);
        Assert.That(
            book.Authorize(
                Owner,
                Bless,
                new Reader(new SpellSlotState(Scoped(PreparedPool), Owner, 0, 1))
            ).IsAuthorized,
            Is.False
        );
        Assert.That(
            book.Authorize(
                Owner,
                Bless,
                new Reader(new SpellSlotState(Scoped(PreparedPool), new CreatureId("other"), 1, 1))
            ).IsAuthorized,
            Is.False
        );
    }

    [Test]
    public void ResourceBindingIsExactAndDoesNotExposeLocalMutableSpending()
    {
        PreparedSpellBook book = CreateBook(
            PreparedSpellEntry.Cantrip(Light),
            PreparedSpellEntry.FromPool(Bless, PreparedPool)
        );

        Assert.That(
            book.BindResource(Owner, Light).Kind,
            Is.EqualTo(SpellCastResourceKind.Cantrip)
        );
        Assert.That(book.BindResource(Owner, Bless).Pool, Is.EqualTo(Scoped(PreparedPool)));
        Assert.That(
            book.BindResource(Owner, new SpellReference(new SpellId("light"), 2)).IsAuthorized,
            Is.False
        );
        Assert.That(
            typeof(PreparedSpellBook).GetMethod("TrySpend"),
            Is.Null,
            "Rules-native books must not expose local mutable spending."
        );
    }

    [Test]
    public void SyntheticRankedSpellRequiresTheExactPreparedRank()
    {
        SpellReference rankThree = new(new SpellId("synthetic-ranked-spell"), 3);
        PreparedSpellBook book = CreateBook(PreparedSpellEntry.FromPool(rankThree, PreparedPool));
        Reader reader = new(new SpellSlotState(Scoped(PreparedPool), Owner, 1, 1));

        Assert.That(book.Authorize(Owner, rankThree, reader).IsAuthorized, Is.True);
        Assert.That(
            book.Authorize(Owner, new SpellReference(rankThree.Spell, 2), reader).IsAuthorized,
            Is.False
        );
        Assert.That(
            book.Authorize(Owner, new SpellReference(rankThree.Spell, 4), reader).IsAuthorized,
            Is.False
        );
    }

    [Test]
    public void InitialSlotStateUsesRequestedOwnerAndDeclaredMaximums()
    {
        PreparedSpellBook book = CreateBook(
            PreparedSpellEntry.FromPool(Bless, PreparedPool),
            PreparedSpellEntry.FromPool(Heal, FontPool)
        );

        IReadOnlyList<SpellSlotState> states = book.CreateInitialSlotStates(Owner);

        Assert.That(states.Count, Is.EqualTo(2));
        Assert.That(states, Has.All.Matches<SpellSlotState>(state => state.Owner == Owner));
        Assert.That(
            states,
            Has.Some.Matches<SpellSlotState>(state =>
                state.Id == Scoped(PreparedPool) && state.Remaining == 1
            )
        );
        Assert.That(
            states,
            Has.Some.Matches<SpellSlotState>(state =>
                state.Id == Scoped(FontPool) && state.Remaining == 4
            )
        );
    }

    private static PreparedSpellBook CreateBook(params PreparedSpellEntry[] entries) =>
        new(
            entries,
            new[]
            {
                new PreparedSpellSlotPool(PreparedPool, 1),
                new PreparedSpellSlotPool(FontPool, 4),
            },
            7
        );

    private static SpellSlotPoolId Scoped(SpellSlotPoolId pool) =>
        new($"{Owner.Value}:{pool.Value}");

    private sealed class Reader : ISpellSlotStateReader
    {
        private readonly Dictionary<SpellSlotPoolId, SpellSlotState> states = new();

        public Reader(params SpellSlotState[] states)
        {
            foreach (SpellSlotState state in states)
                this.states.Add(state.Id, state);
        }

        public bool TryGet(SpellSlotPoolId pool, out SpellSlotState state) =>
            states.TryGetValue(pool, out state);
    }
}
