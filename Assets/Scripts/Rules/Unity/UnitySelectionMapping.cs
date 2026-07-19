using System;
using System.Collections.Generic;
using Game.Rules.Runtime;
using UnityEngine;

namespace Game.Rules.Unity
{
    /// <summary>
    /// Captures an area choice in Unity coordinates before it crosses into stable rules values.
    /// </summary>
    public sealed class UnityAreaSelection
    {
        /// <summary>
        /// Initializes a Unity-side area choice.
        /// </summary>
        /// <param name="template">The stable template selected by the player.</param>
        /// <param name="origin">The Unity grid coordinate at which the template originates.</param>
        /// <param name="facing">The Unity grid coordinate that determines orientation.</param>
        /// <exception cref="ArgumentException"><paramref name="template"/> is uninitialized.</exception>
        public UnityAreaSelection(AreaTemplateId template, Vector3Int origin, Vector3Int facing)
        {
            if (template.IsEmpty)
                throw new ArgumentException(
                    "An area selection requires a template.",
                    nameof(template)
                );

            Template = template;
            Origin = origin;
            Facing = facing;
        }

        /// <summary>
        /// Gets the stable template chosen from the request.
        /// </summary>
        public AreaTemplateId Template { get; }

        /// <summary>
        /// Gets the Unity grid coordinate at which the template originates.
        /// </summary>
        public Vector3Int Origin { get; }

        /// <summary>
        /// Gets the Unity grid coordinate that determines the template's facing.
        /// </summary>
        public Vector3Int Facing { get; }
    }

    /// <summary>
    /// Maps scene references and Unity grid values into stable, Unity-free selection values.
    /// </summary>
    /// <remarks>
    /// An encounter composition root supplies this mapper. Implementations may consult encounter-local
    /// bindings but must not use a singleton or global identity map. Every returned selection must own
    /// any collection it exposes so later scene or input mutations cannot alter a completed workflow.
    /// </remarks>
    public interface IUnitySelectionMapper
    {
        /// <summary>
        /// Maps a selected creature object to its stable rules identity.
        /// </summary>
        CreatureSelection MapCreature(GameObject sceneObject);

        /// <summary>
        /// Maps selected creature objects to an ordered, defensively owned stable selection.
        /// </summary>
        MultipleCreatureSelection MapCreatures(IReadOnlyList<GameObject> sceneObjects);

        /// <summary>
        /// Maps a selected item object to its stable rules identity.
        /// </summary>
        ItemSelection MapItem(GameObject sceneObject);

        /// <summary>
        /// Maps a selected weapon object to its stable rules item identity.
        /// </summary>
        WeaponSelection MapWeapon(GameObject sceneObject);

        /// <summary>
        /// Maps an ordered Unity path to stable grid positions and owns the resulting sequence.
        /// </summary>
        PathSelection MapPath(IReadOnlyList<Vector3Int> path);

        /// <summary>
        /// Maps a Unity grid cell to a stable rules grid position.
        /// </summary>
        GridCellSelection MapGridCell(Vector3Int cell);

        /// <summary>
        /// Maps a Unity-side template placement and orientation to stable area values.
        /// </summary>
        AreaSelection MapArea(UnityAreaSelection area);
    }
}
