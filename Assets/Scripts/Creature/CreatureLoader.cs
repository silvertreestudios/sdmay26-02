// using UnityEngine;
// using Game.Creature;

// namespace Game.Creature
// {
//     public class CreatureLoader : MonoBehaviour
//     {
//         // Optional: editable in Inspector
//         public string creatureName = "goblin-warrior";

//         void Start()
//         {
//             // Direct name-based creation � no relative path required
//             GameObject creature = DataFileInterface.GetCreature(creatureName);
//             if (creature == null)
//             {
//                 Debug.LogError($"Failed to load creature '{creatureName}'. Ensure a DataFileInterface exists in the scene and the JSON is present under Assets/DataFiles.");
//                 return;
//             }
//             // extra scripts/modifications applied manually as prefab
//             // unique ID?   .getInstanceID()?  
//             // unique name? IE: "Goblin Warrior A", can be applied manually in editor
//             // TODO: automatic naming for summonable creatures
//             creature.transform.position = Vector3.zero;
//             Debug.Log("Loaded creature: " + creature.name);
//         }
//     }
// }