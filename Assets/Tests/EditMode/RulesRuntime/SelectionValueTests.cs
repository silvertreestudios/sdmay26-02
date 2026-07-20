using System;
using NUnit.Framework;

namespace Game.Rules.Runtime.Tests
{
    /// <summary>Verifies the structural values retained by the generic selection framework.</summary>
    public sealed class SelectionValueTests
    {
        /// <summary>Verifies availability cannot pair an available state with a failure reason.</summary>
        [Test]
        public void AvailabilityStatesAreStructural()
        {
            UnavailableActionAvailability unavailable = ActionAvailability.Unavailable(
                "No actions remain."
            );

            Assert.That(ActionAvailability.Available, Is.TypeOf<AvailableActionAvailability>());
            Assert.That(unavailable.Reason, Is.EqualTo("No actions remain."));
            Assert.Throws<ArgumentException>(() => ActionAvailability.Unavailable(" "));
        }

        /// <summary>Verifies each workflow outcome exposes only data valid for that state.</summary>
        [Test]
        public void SelectionOutcomesAreStructural()
        {
            CompletedSelectionOutcome<int> completed = SelectionOutcome<int>.Completed(7);
            InvalidSelectionOutcome<int> invalid = SelectionOutcome<int>.Invalid("Rejected.");

            Assert.That(completed.Selection, Is.EqualTo(7));
            Assert.That(
                SelectionOutcome<int>.Cancelled,
                Is.TypeOf<CancelledSelectionOutcome<int>>()
            );
            Assert.That(invalid.Reason, Is.EqualTo("Rejected."));
            Assert.Throws<ArgumentNullException>(() => SelectionOutcome<string>.Completed(null));
            Assert.Throws<ArgumentException>(() => SelectionOutcome<int>.Invalid(""));
        }

        /// <summary>Verifies ordered results cannot silently contain absent reference selections.</summary>
        [Test]
        public void OrderedSelectionRejectsAbsentReferenceValues()
        {
            Assert.Throws<ArgumentNullException>(() => new OrderedSelection<string, int>(null, 1));
            Assert.Throws<ArgumentNullException>(() => new OrderedSelection<int, string>(1, null));
        }

        /// <summary>Verifies the concrete request owns its feature-specific boundary validation.</summary>
        [Test]
        public void GenericRequestDelegatesValidationToConcreteContract()
        {
            TestSelectionRequest<int> request = new TestSelectionRequest<int>(
                "positive-value",
                value => value > 0
            );

            Assert.That(request.Accepts(1), Is.True);
            Assert.That(request.Accepts(0), Is.False);
        }
    }
}
