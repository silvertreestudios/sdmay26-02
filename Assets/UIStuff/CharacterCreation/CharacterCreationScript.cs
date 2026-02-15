using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
    
//Inherits from class `MonoBehaviour`. This makes it attachable to a game object as a component.
public class CharacterCreationScript : MonoBehaviour
{

    RadioButtonGroup ancestryRadioButtonGroup;
    RadioButtonGroup heritageRadioButtonGroup;
    RadioButtonGroup ancestryFeatsRadioButtonGroup;
    Dictionary<string, List<string>> heritagesByAncestry;
    Dictionary<string, List<string>> ancestryFeatsByAncestry;

    private void OnEnable()
    {

        UIDocument menu = GetComponent<UIDocument>();
        VisualElement root = menu.rootVisualElement;

        ancestryRadioButtonGroup = root.Q<RadioButtonGroup>("AncestryRadioButtonGroup");
        heritageRadioButtonGroup = root.Q<RadioButtonGroup>("HeritageRadioButtonGroup");
        ancestryFeatsRadioButtonGroup = root.Q<RadioButtonGroup>("AncestryFeatsRadioButtonGroup");
        ancestryRadioButtonGroup.value = 0;

        heritagesByAncestry = new Dictionary<string, List<string>>()
        {
            { "Elf", new List<string> { "Ancient", "Arctic", "Carvern", "Seer", "Whisper", "Woodland" } },
            { "Gnome", new List<string> { "Chameleon", "Fey-touched", "Sensate", "Umbral", "Wellspring" } },
            { "Human", new List<string> { "Skilled", "Versatile" } },
            { "Dwarf", new List<string> { "Ancient-Blooded", "Death Warden", "Forge", "Rock", "Strong-Blooded" } },
            { "Goblin", new List<string> { "Charhide", "Irongut", "Razortooth", "Snow", "Unbreakable" } },
            { "Halfling", new List<string> { "Gusty", "Hillock", "Nomadic", "Twilight", "Wildwood" } },
            { "Leshy", new List<string> { "Cactus", "Fruit", "Fungus", "Gourd", "Leaf", "Lotus", "Root", "Seaweed", "Vine" } },
            { "Orc", new List<string> { "Badlands", "Battle-Ready", "Deep", "Grave", "Hold-Scarred", "Rainfall", "Winter" } }
        };

        ancestryFeatsByAncestry = new Dictionary<string, List<string>>()
        {
            { "Elf", new List<string> { "Ancestral Longevity", "Elven Weapon Familiarity", "Forlorn", "Nimble Elf", "Otherwordly Magic", "Unwavering Mien" } },
            { "Gnome", new List<string> { "Animal Accomplice", "Animal Elocutionist", "Fey Fellowship", "First World Magic", "Gnome Obsession", "Gnome Weapon Familiarity", "Illusion Sense", "Razzle-Dazzle" } },
            { "Human", new List<string> { "Adapted Cantrip", "Cooperative Nature", "General Training", "Naughty Obstinacy", "Natural Ambition", "Natural Skill", "Unconventional Weaponry" } },
            { "Dwarf", new List<string> { "Dwarven Doughtiness", "Dwarven Weapon Familiarity", "Mountain Strategy", "Rock Runner", "Stonemason's Eye", "Unburdended Iron" } },
            { "Goblin", new List<string> { "Burn It!", "City Scavenger", "Goblin Scuttle", "Goblin Song", "Goblin Weapon Familiarity", "Junk Tinker", "Rough Rider", "Very Sneaky" } },
            { "Halfling", new List<string> { "Distracting Shadows", "Folksy Patter", "Halfling Luck", "Prairie Rider", "Sure Feed", "Titan Slinger", "Unfettered Halfling", "Watchful Halfling", "Halfling Weapon Familiarity" } },
            { "Leshy", new List<string> { "Grasping Reach", "Harmlessly Cute", "Leshy Superstition", "Seedpod", "Shadow of the Wilds", "Undaunted" } },
            { "Orc", new List<string> { "Beast Trainer", "Iron Fists", "Orc Ferocity", "Orc Superstition", "Hold Mark", "Orc Weapon Familiarity", "Tusks" } }
        };

        ancestryRadioButtonGroup.RegisterValueChangedCallback(OnAncestryChanged);

        //INITIAL LISTVIEW STUFF BELOW


        // 1. Define the Data Source
        List<string> myItems = new List<string> {"Dexterity", "Intelligence", "Free"};

        // 2. Create the ListView (or query it from UXML)
        ListView myListView = root.Q<ListView>("AncestryAttributeBoostsList"); 
        // If not using UXML, instantiate: ListView myListView = new ListView();
        // and add it to the root: root.Add(myListView);

        // 3. Configure the ListView
        myListView.itemsSource = myItems;
        myListView.fixedItemHeight = 20;

        myListView.makeItem = () =>
        {
            Label itemLabel = new Label();
            itemLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            return itemLabel;
        };

        myListView.bindItem = (element, index) =>
        {
            (element as Label).text = myItems[index];
        };
    }

    //when there's a change in the ancestryRadioGroup, grab that text
    void OnAncestryChanged(ChangeEvent<int> evt)
    {
        string selectedAncestry = (ancestryRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateHeritageButtons(selectedAncestry);
        PopulateAncestryFeatButtons(selectedAncestry);
    }

    void PopulateHeritageButtons(string ancestry)
    {
        //Debug.Log($"Ancestry = '{ancestry}'"); //debug to see what ancestry is being passed
        
        heritageRadioButtonGroup.Clear(); //clear out past buttons

        if (!heritagesByAncestry.TryGetValue(ancestry, out var heritages))
            return;

        foreach (string heritage in heritages)
        {
            var rb = new RadioButton
            {
                text = heritage
            };

            heritageRadioButtonGroup.Add(rb);
        }

        heritageRadioButtonGroup.value = 0; // optional: auto-select first
    }

    void PopulateAncestryFeatButtons(string ancestry)
    {        
        ancestryFeatsRadioButtonGroup.Clear(); //clear out past buttons

        if (!ancestryFeatsByAncestry.TryGetValue(ancestry, out var ancestryFeats))
            return;

        foreach (string ancestryFeat in ancestryFeats)
        {
            var rb = new RadioButton
            {
                text = ancestryFeat
            };

            ancestryFeatsRadioButtonGroup.Add(rb);
        }

        ancestryFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }
}
