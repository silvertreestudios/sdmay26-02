using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
    
//Inherits from class `MonoBehaviour`. This makes it attachable to a game object as a component.
public class CharacterCreationScript : MonoBehaviour
{

    RadioButtonGroup ancestryRadioButtonGroup;
    RadioButtonGroup heritageRadioButtonGroup;
    RadioButtonGroup ancestryFeatsRadioButtonGroup;
    RadioButtonGroup backgroundRadioButtonGroup;
    RadioButtonGroup backgroundBoostChoiceRadioButtonGroup;
    RadioButtonGroup classFeatsRadioButtonGroup;
    RadioButtonGroup classesRadioButtonGroup;
    Label backgroundDescriptionLabel;
    Label backgroundSkillLabel;
    Label backgroundSkillFeatLabel;
    Dictionary<string, List<string>> heritagesByAncestry;
    Dictionary<string, List<string>> ancestryFeatsByAncestry;
    Dictionary<string, List<string>> backgroundDescriptionByBackground;
    Dictionary<string, List<string>> classFeatByClass;

    private void OnEnable()
    {

        UIDocument menu = GetComponent<UIDocument>();
        VisualElement root = menu.rootVisualElement;

        ancestryRadioButtonGroup = root.Q<RadioButtonGroup>("AncestryRadioButtonGroup");
        heritageRadioButtonGroup = root.Q<RadioButtonGroup>("HeritageRadioButtonGroup");
        ancestryFeatsRadioButtonGroup = root.Q<RadioButtonGroup>("AncestryFeatsRadioButtonGroup");
        backgroundRadioButtonGroup = root.Q<RadioButtonGroup>("BackgroundRadioButtonGroup");
        backgroundBoostChoiceRadioButtonGroup = root.Q<RadioButtonGroup>("BackgroundBoostChoiceRadioButtonGroup");
        backgroundDescriptionLabel = root.Q<Label>("BackgroundDescriptionLabel");
        backgroundSkillLabel = root.Q<Label>("BackgroundSkillLabel");
        backgroundSkillFeatLabel = root.Q<Label>("BackgroundSkillFeatLabel");
        classFeatsRadioButtonGroup = root.Q<RadioButtonGroup>("ClassFeatsRadioButtonGroup");
        classesRadioButtonGroup = root.Q<RadioButtonGroup>("ClassesRadioButtonGroup");
        //ancestryRadioButtonGroup.value = 0;

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

        backgroundDescriptionByBackground = new Dictionary<string, List<string>>()
        {
            {"Acolyte", new List<string> {"You spent your early days in a religious monastery or cloister. You may have traveled out into the world to spread the message of your religion or because you cast away the teachings of your faith, but deep down you'll always carry within you the lessons you learned.", "Intelligence", "Wisdom", "Religion", "Student of the Canon"}},
            {"Bandit", new List<string> {"Your past includes no small amount of rural banditry, robbing travelers on the road and scraping by. Whether your robbery was sanctioned by a local noble or you did so of your own accord, you eventually got caught up in the adventuring life. Now, adventure is your stock and trade, and years of camping and skirmishing have only helped.", "Dexterity", "Charisma", "Intimidation", "Group Coercion"}},
            {"Cook", new List<string> {"You grew up in the kitchens of a tavern or other dining establishment and excelled there, becoming an exceptional cook. Baking, cooking, a little brewing on the side—you've spent lots of time out of sight. It's about time you went out into the world to catch some sights for yourself", "Constitution", "Intelligence", "Survival", "Seasoned"}}
        };

        classFeatByClass = new Dictionary<string, List<string>>()
        {
            {"Fighter", new List<string> {"Combat Assessment", "Double Slice", "Exacting Strike", "Point Blank Stance", "Reactive Shield", "Snagging Strike", "Sudden Charge", "Vicious Swing"}},
            {"Cleric", new List<string> {"Deadly Simplicity", "Divine Castigation", "Domain Initiate", "Harming Hands", "Healing Hands", "Premonition of Avoidance", "Reach Spell"}},
            {"Rogue", new List<string> {"Nimble Dodge", "Overextending Feint", "Plant Evidence", "Trap Finder", "Tumble Behind", "Twin Feint", "You're Next"}},
            {"Sorcerer", new List<string> {"Blood Rising", "Familiar", "Reach Spell", "Tap into Blood", "Widen Spell"}}  
        };

        ancestryRadioButtonGroup.RegisterValueChangedCallback(OnAncestryChanged);
        backgroundRadioButtonGroup.RegisterValueChangedCallback(OnBackgroundChanged);
        classesRadioButtonGroup.RegisterValueChangedCallback(OnClassChanged);


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

    //when there's a change in the backgroundRadioGroup, grab that text
    void OnBackgroundChanged(ChangeEvent<int> evt)
    {
        string selectedBackground = (backgroundRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateBackgroundInfo(selectedBackground);
    }

    void PopulateBackgroundInfo(string background)
    {
        //skill -> red box
        //skill feat -> red box

        backgroundBoostChoiceRadioButtonGroup.Clear();

        if (!backgroundDescriptionByBackground.TryGetValue(background, out var backgroundBoost))
            return;

        backgroundDescriptionLabel.text = backgroundDescriptionByBackground[background][0];
        backgroundSkillLabel.text = "Skill: " + backgroundDescriptionByBackground[background][3];
        backgroundSkillFeatLabel.text = "Skill Feat: " + backgroundDescriptionByBackground[background][4];

        //make a new button where the text is the 1st boost
        var rb = new RadioButton
        {
            text = backgroundDescriptionByBackground[background][1] //set description text
        };
        //make a new button where the text is the 2nd boost
        var rb2 = new RadioButton
        {
            text = backgroundDescriptionByBackground[background][2]
        };

        backgroundBoostChoiceRadioButtonGroup.Add(rb);
        backgroundBoostChoiceRadioButtonGroup.Add(rb2);

    }

    void OnClassChanged(ChangeEvent<int> evt)
    {
        string selectedClass = (classesRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateClassFeatButtons(selectedClass);
    }
 
    void PopulateClassFeatButtons(string className)
    {
        classFeatsRadioButtonGroup.Clear(); //clear out past buttons

        if (!classFeatByClass.TryGetValue(className, out var classFeats))
            return;

        foreach (string classFeat in classFeats)
        {
            var rb = new RadioButton
            {
                text = classFeat
            };

            classFeatsRadioButtonGroup.Add(rb);
        }

        classFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }
}
