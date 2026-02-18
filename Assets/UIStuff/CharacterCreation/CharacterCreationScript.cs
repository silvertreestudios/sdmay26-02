using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

[System.Serializable]
public class Ancestry
{
    public string id;
    public string description;
    public string[] attributeBoost;
    public string attributeFlaw;
    public int hp;
    public int speed;
    public string[] ancestryFeat;
    public string[] heritage;
    public string size;
    public string[] languages;
    public string[] traits;
    public string[] specialAbilities;
}

[System.Serializable]
public class AncestryDatabase
 {
    public List<Ancestry> ancestries;
}
    
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
    Label ancestryDescriptionLabel;
    Label ancestryBoostsFlawsLabel;
    TextField ancestryChoiceField;
    TextField heritageChoiceField;
    Dictionary<string, List<string>> backgroundDescriptionByBackground;
    Dictionary<string, List<string>> classFeatByClass;

    //for json
    TextAsset jsonFile;
    AncestryDatabase db;
    //for json end

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
        ancestryDescriptionLabel = root.Q<Label>("AncestryDescription");
        ancestryBoostsFlawsLabel = root.Q<Label>("AncestryBoostsFlaws");
        ancestryChoiceField = root.Q<TextField>("AncestryChoice");
        heritageChoiceField = root.Q<TextField>("HeritageChoice"); //NOT CURRENTLY TIED TO ANYTHING

        //for json pt2, assigning
        jsonFile = Resources.Load<TextAsset>("Data/ancestry");
        db = JsonUtility.FromJson<AncestryDatabase>(jsonFile.text);

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
    }

    //when there's a change in the ancestryRadioGroup, grab that text
    void OnAncestryChanged(ChangeEvent<int> evt)
    {
        string selectedAncestry = (ancestryRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        ancestryChoiceField.value = selectedAncestry;
        PopulateHeritageButtons(selectedAncestry);
        PopulateAncestryFeatButtons(selectedAncestry);
        PopulateAncestryDescription(selectedAncestry);
    }

    //works with json
    void PopulateHeritageButtons(string ancestry)
    {

        heritageRadioButtonGroup.Clear(); //clear out past buttons

        //find a match between the passed-in ancestry and the db.ancestries list, then print out the heritage list of that ancestry
        //find matching ancestry by id
        Ancestry selectedAncestry = db.ancestries.Find(a => a.id == ancestry);
        foreach (string heritage in selectedAncestry.heritage)
        {
            var rb = new RadioButton
            {
                text = heritage
            };
            heritageRadioButtonGroup.Add(rb);
        }

        heritageRadioButtonGroup.value = 0; // optional: auto-select first

    }

    //works with json
    void PopulateAncestryFeatButtons(string ancestry)
    {        
        ancestryFeatsRadioButtonGroup.Clear(); //clear out past buttons

        Ancestry selectedAncestry = db.ancestries.Find(a => a.id == ancestry);
        foreach (string ancestryFeat in selectedAncestry.ancestryFeat)
        {
            var rb = new RadioButton
            {
                text = ancestryFeat
            };
            ancestryFeatsRadioButtonGroup.Add(rb);
        }

        ancestryFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }

    //also populates attribute boosts and flaws
    void PopulateAncestryDescription(string ancestry)
    {
        Ancestry selectedAncestry = db.ancestries.Find(a => a.id == ancestry);
        ancestryDescriptionLabel.text = "Description: " + selectedAncestry.description;

        ancestryBoostsFlawsLabel.text = "Attribute Boosts: " + string.Join(", ", selectedAncestry.attributeBoost) + "\n" + "Attribute Flaw: " + selectedAncestry.attributeFlaw;
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
