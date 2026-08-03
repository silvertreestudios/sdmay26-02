using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

/// <summary>Verifies the player-card action-resource presentation seam.</summary>
public class ActionMedallionPresenterTests
{
    /// <summary>Verifies standard and Quickened fill states are rendered independently.</summary>
    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public void RenderFillsStandardAndQuickenedMedallionsIndependently(
        int standardActionsRemaining,
        bool quickenedResourceAvailable
    )
    {
        VisualElement card = CreateCard();

        ActionMedallionPresenter.Render(card, standardActionsRemaining, quickenedResourceAvailable);

        List<VisualElement> standardMedallions = card.Query<VisualElement>(
                className: ActionMedallionPresenter.StandardMedallionClass
            )
            .ToList();
        VisualElement quickenedMedallion = card.Q<VisualElement>(
            className: ActionMedallionPresenter.QuickenedMedallionClass
        );
        Assert.That(
            standardMedallions.Count(medallion =>
                medallion.ClassListContains(ActionMedallionPresenter.FilledClass)
            ),
            Is.EqualTo(standardActionsRemaining)
        );
        Assert.That(
            standardMedallions.Count(medallion =>
                medallion.ClassListContains(ActionMedallionPresenter.EmptyClass)
            ),
            Is.EqualTo(ActionMedallionPresenter.StandardMedallionCount - standardActionsRemaining)
        );
        Assert.That(
            quickenedMedallion.ClassListContains(ActionMedallionPresenter.FilledClass),
            Is.EqualTo(quickenedResourceAvailable)
        );
        Assert.That(
            quickenedMedallion.ClassListContains(ActionMedallionPresenter.EmptyClass),
            Is.EqualTo(!quickenedResourceAvailable)
        );
        Assert.That(quickenedMedallion.parent, Is.SameAs(card));
    }

    /// <summary>Verifies spending the Quickened resource empties its persistent fourth slot.</summary>
    [Test]
    public void RenderKeepsSpentQuickenedSlotEmptyAndPresent()
    {
        VisualElement card = CreateCard();
        VisualElement quickenedMedallion = card.Q<VisualElement>(
            className: ActionMedallionPresenter.QuickenedMedallionClass
        );
        ActionMedallionPresenter.Render(card, 3, quickenedResourceAvailable: true);

        ActionMedallionPresenter.Render(card, 2, quickenedResourceAvailable: false);

        Assert.That(quickenedMedallion.parent, Is.SameAs(card));
        Assert.That(
            quickenedMedallion.ClassListContains(ActionMedallionPresenter.FilledClass),
            Is.False
        );
        Assert.That(
            quickenedMedallion.ClassListContains(ActionMedallionPresenter.EmptyClass),
            Is.True
        );
        Assert.That(
            card.Query<VisualElement>(className: ActionMedallionPresenter.StandardMedallionClass)
                .ToList()
                .Count(medallion =>
                    medallion.ClassListContains(ActionMedallionPresenter.FilledClass)
                ),
            Is.EqualTo(2)
        );
    }

    private static VisualElement CreateCard()
    {
        VisualElement card = new();
        for (int i = 0; i < ActionMedallionPresenter.StandardMedallionCount; i++)
        {
            VisualElement medallion = new();
            medallion.AddToClassList(ActionMedallionPresenter.StandardMedallionClass);
            card.Add(medallion);
        }

        VisualElement quickenedMedallion = new();
        quickenedMedallion.AddToClassList(ActionMedallionPresenter.QuickenedMedallionClass);
        card.Add(quickenedMedallion);
        return card;
    }
}
