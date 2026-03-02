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

[System.Serializable]
public class ClassInfo
{
    public string id;
    public string description;
    public string attributeBoost;
    public int hp;
    public string perception;
    public string fortitude;
    public string reflex;
    public string will;
    public List<string> skills;
    public Attacks attacks; // attack name -> proficiency level
    public List<string> defenses;
    public string spells;
    public List<string> subclass;
    public List<string> classFeat;
}

//because I made attacks nested in the json
[System.Serializable]
public class Attacks
{
    public string simpleWeapons;
    public string martialWeapons;
    public string advancedWeapons;
    public string unarmedAttacks;
}

[System.Serializable]
public class ClassDatabase
{
    public List<ClassInfo> classes;
}

[System.Serializable]
public class PlayerCharacter
{
    public string name;
    public string gender;
    public string ancestry;
    public string heritage;
    public string background;
    public string className;
    public int hp;
    public int speed;
    public int strength;
    public int dexterity;
    public int constitution;
    public int intelligence;
    public int wisdom;
    public int charisma;
    public string perception;
    public string fortitude;
    public string reflex;
    public string will;
    public string ancestryFeat;
    public string classFeat;
    public string subclass;
}

//individually track the attribute boosts from ancestry, background, and class. Not json related
[System.Serializable]
public class AttributeContributions
{
    public int ancestry;
    public int background;
    public int className;
    public int freeChoice;
    public int attributeTotal => ancestry + background + className + freeChoice;
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
    RadioButtonGroup subclassRadioButtonGroup;
    Label backgroundDescriptionLabel;
    Label backgroundSkillLabel;
    Label backgroundSkillFeatLabel;
    Label ancestryDescriptionLabel;
    Label ancestryBoostsFlawsLabel;
    Label ancestrySpecialAbilitiesLabel;
    Label classDescriptionLabel;
    Label classBoostsLabel;
    Label classSkillsLabel;
    TextField ancestryChoiceField;
    TextField heritageChoiceField;
    TextField backgroundChoiceField;
    TextField classChoiceField;
    IntegerField hpField;
    IntegerField speedField;
    IntegerField strengthAttributeField;
    IntegerField dexterityAttributeField;
    IntegerField constitutionAttributeField;
    IntegerField intelligenceAttributeField;
    IntegerField wisdomAttributeField;
    IntegerField charismaAttributeField;
    TextField perceptionField;
    TextField fortitudeField;
    TextField reflexField;
    TextField willField;
    TextField simpleWeaponsField;
    TextField martialWeaponsField;
    TextField advancedWeaponsField;
    TextField unarmedAttackField;
    TextField unarmoredDefenseField;
    TextField lightArmorField;
    TextField mediumArmorField;
    TextField allArmorField;
    Toggle strengthToggle;
    Toggle dexterityToggle;
    Toggle constitutionToggle;
    Toggle intelligenceToggle;
    Toggle wisdomToggle;
    Toggle charismaToggle;
    Dictionary<string, List<string>> backgroundDescriptionByBackground;
    List<Toggle> toggles;
    List<string> attributeKeysForToggles; //the index of the List<Toggle> matches the index of List<string> attributeKey
    HashSet<string> selectedAttributes; //HashSet was recommended...
    int maxSelections = 4;
    //int selectedCount = 0;
    int classHP;
    int ancestryHP;
    Dictionary<string, AttributeContributions> attributes; //string is the attribute, AttributeContributions is ancestry/background/class/freeChoice; main storage for current attributes

    //for json
    TextAsset jsonFile;
    TextAsset jsonFile2;
    AncestryDatabase db;
    ClassDatabase db2;
    //for json end
    PlayerCharacter currentCharacter;
    string jsonFile3;

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
        ancestrySpecialAbilitiesLabel = root.Q<Label>("AncestrySpecialAbilities");
        ancestryChoiceField = root.Q<TextField>("AncestryChoice");
        heritageChoiceField = root.Q<TextField>("HeritageChoice");
        hpField = root.Q<IntegerField>("HP");
        speedField = root.Q<IntegerField>("Speed");
        subclassRadioButtonGroup = root.Q<RadioButtonGroup>("SubClassRadioButtonGroup");
        classDescriptionLabel = root.Q<Label>("ClassDescription");
        classBoostsLabel = root.Q<Label>("ClassBoosts");
        classSkillsLabel = root.Q<Label>("ClassSkills");
        perceptionField = root.Q<TextField>("Perception");
        fortitudeField = root.Q<TextField>("Fortitude");
        reflexField = root.Q<TextField>("Reflex");
        willField = root.Q<TextField>("Will");
        backgroundChoiceField = root.Q<TextField>("BackgroundChoice");
        classChoiceField = root.Q<TextField>("ClassChoice");
        strengthToggle = root.Q<Toggle>("StrengthToggle");
        dexterityToggle = root.Q<Toggle>("DexterityToggle");
        constitutionToggle = root.Q<Toggle>("ConstitutionToggle");
        intelligenceToggle = root.Q<Toggle>("IntelligenceToggle");
        wisdomToggle = root.Q<Toggle>("WisdomToggle");
        charismaToggle = root.Q<Toggle>("CharismaToggle");
        simpleWeaponsField = root.Q<TextField>("SimpleAttack");
        martialWeaponsField = root.Q<TextField>("MartialAttack");
        advancedWeaponsField = root.Q<TextField>("AdvancedAttack");
        unarmedAttackField = root.Q<TextField>("UnarmedAttack");
        unarmoredDefenseField = root.Q<TextField>("UnarmoredDefense");
        lightArmorField = root.Q<TextField>("LightArmorDefense");
        mediumArmorField = root.Q<TextField>("MediumArmorDefense");
        allArmorField = root.Q<TextField>("AllArmorDefense");
        strengthAttributeField = root.Q<IntegerField>("StrengthAttribute");
        dexterityAttributeField = root.Q<IntegerField>("DexterityAttribute");
        constitutionAttributeField = root.Q<IntegerField>("ConstitutionAttribute");
        intelligenceAttributeField = root.Q<IntegerField>("IntelligenceAttribute");
        wisdomAttributeField = root.Q<IntegerField>("WisdomAttribute");
        charismaAttributeField = root.Q<IntegerField>("CharismaAttribute"); 

        //for json, assigning
        jsonFile = Resources.Load<TextAsset>("Data/ancestry");
        db = JsonUtility.FromJson<AncestryDatabase>(jsonFile.text);

        jsonFile2 = Resources.Load<TextAsset>("Data/class");
        db2 = JsonUtility.FromJson<ClassDatabase>(jsonFile2.text);

        //TESTING PLAYERCHARACTER JSON
        currentCharacter = new PlayerCharacter();
        //currentCharacter.ancestry = "elf";
        jsonFile3 = JsonUtility.ToJson(currentCharacter);

        //small enough that I'm keeping as a dictionary for now
        backgroundDescriptionByBackground = new Dictionary<string, List<string>>()
        {
            {"Acolyte", new List<string> {"You spent your early days in a religious monastery or cloister. You may have traveled out into the world to spread the message of your religion or because you cast away the teachings of your faith, but deep down you'll always carry within you the lessons you learned.", "intelligence", "wisdom", "Religion", "Student of the Canon"}},
            {"Bandit", new List<string> {"Your past includes no small amount of rural banditry, robbing travelers on the road and scraping by. Whether your robbery was sanctioned by a local noble or you did so of your own accord, you eventually got caught up in the adventuring life. Now, adventure is your stock and trade, and years of camping and skirmishing have only helped.", "dexterity", "charisma", "Intimidation", "Group Coercion"}},
            {"Cook", new List<string> {"You grew up in the kitchens of a tavern or other dining establishment and excelled there, becoming an exceptional cook. Baking, cooking, a little brewing on the side—you've spent lots of time out of sight. It's about time you went out into the world to catch some sights for yourself", "constitution", "intelligence", "Survival", "Seasoned"}}
        };

        //initialize "attributes" dictionary with all 6 attributes, each with an AttributeContributions object
        attributes = new Dictionary<string, AttributeContributions>();
        attributes.Add("strength", new AttributeContributions());
        attributes.Add("dexterity", new AttributeContributions());
        attributes.Add("constitution", new AttributeContributions());
        attributes.Add("intelligence", new AttributeContributions());
        attributes.Add("wisdom", new AttributeContributions());
        attributes.Add("charisma", new AttributeContributions());

        ancestryRadioButtonGroup.RegisterValueChangedCallback(OnAncestryChanged);
        backgroundRadioButtonGroup.RegisterValueChangedCallback(OnBackgroundChanged);
        classesRadioButtonGroup.RegisterValueChangedCallback(OnClassChanged);
        heritageRadioButtonGroup.RegisterValueChangedCallback(OnHeritageChanged);

        //no special grouping for toggles, so I'm handling them as list manually
        selectedAttributes = new();
        toggles = new List<Toggle> { strengthToggle, dexterityToggle, constitutionToggle, intelligenceToggle, wisdomToggle, charismaToggle };
        attributeKeysForToggles = new List<string> {"strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"};
        foreach (var toggle in toggles)
        {
            toggle.RegisterValueChangedCallback(evt =>
            {
                HandleToggleChanged(toggle, evt.newValue);
            });
        }
    }

    //when there's a change in the ancestryRadioGroup, grab that text
    void OnAncestryChanged(ChangeEvent<int> evt)
    {
        ancestryHP = 0; //resets the ancestry hp every the ancestry changes so it doesn't keep adding up

        string selectedAncestry = (ancestryRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        ancestryChoiceField.value = selectedAncestry;
        ancestryHP = db.ancestries.Find(a => a.id == selectedAncestry).hp;
        hpField.value = classHP + ancestryHP; //add ancestry hp to other hp

        Ancestry selectedAncestryObj = db.ancestries.Find(a => a.id == selectedAncestry); //make an ancestry object
        List<string> boosts = new List<string>(selectedAncestryObj.attributeBoost); //make a list of that ancestry's boosts
        ClearAncestryContributions();
        ApplyAncestryBoosts(boosts); //pass in List of strings
        RefreshAttributeFields();

        speedField.value = db.ancestries.Find(a => a.id == selectedAncestry).speed;
        PopulateHeritageButtons(selectedAncestry);
        PopulateAncestryFeatButtons(selectedAncestry);
        PopulateAncestryDescription(selectedAncestry);

        currentCharacter.ancestry = selectedAncestry;
        Debug.Log(JsonUtility.ToJson(currentCharacter, true));
    }

    //when there's a change in the heritageRadioGroup, grab that text
    void OnHeritageChanged(ChangeEvent<int> evt)
    {
        //guards against -1, which is when no button is selected
        if (evt.newValue < 0) {
            return;
        }

        string selectedHeritage = (heritageRadioButtonGroup[evt.newValue] as RadioButton).text; //for some reason .label doesn't work here but .text does
        heritageChoiceField.value = selectedHeritage;
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
        ancestrySpecialAbilitiesLabel.text = "Special Abilities: " + string.Join(", ", selectedAncestry.specialAbilities);
    }

    //when there's a change in the backgroundRadioGroup, grab that text
    void OnBackgroundChanged(ChangeEvent<int> evt)
    {
        string selectedBackground = (backgroundRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateBackgroundInfo(selectedBackground);
        backgroundChoiceField.value = selectedBackground;

        //handle attributes 
        List<string> boosts = new List<string>{backgroundDescriptionByBackground[selectedBackground][1], backgroundDescriptionByBackground[selectedBackground][2]}; //make a list of that background's boosts
        ClearBackgroundContributions();
        ApplyBackgroundBoosts(boosts); //pass in List of strings
        RefreshAttributeFields();
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
        PopulateSubclassButtons(selectedClass);
        PopulateClassDescription(selectedClass);

        classChoiceField.value = selectedClass;

        classHP = 0;
        classHP = db2.classes.Find(a => a.id == selectedClass).hp;
        hpField.value = ancestryHP + classHP; //add class hp to total hp

        simpleWeaponsField.value = db2.classes.Find(a => a.id == selectedClass).attacks.simpleWeapons;
        martialWeaponsField.value = db2.classes.Find(a => a.id == selectedClass).attacks.martialWeapons;
        advancedWeaponsField.value = db2.classes.Find(a => a.id == selectedClass).attacks.advancedWeapons;
        unarmedAttackField.value = db2.classes.Find(a => a.id == selectedClass).attacks.unarmedAttacks;

        unarmoredDefenseField.value = db2.classes.Find(a => a.id == selectedClass).defenses.Contains("unarmored defense") ? "trained" : "untrained";
        lightArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.Contains("light armor") ? "trained" : "untrained";
        mediumArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.Contains("medium armor") ? "trained" : "untrained";
        allArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.Contains("all armor") ? "trained" : "untrained";

        perceptionField.value = db2.classes.Find(a => a.id == selectedClass).perception;
        fortitudeField.value = db2.classes.Find(a => a.id == selectedClass).fortitude;
        reflexField.value = db2.classes.Find(a => a.id == selectedClass).reflex;
        willField.value = db2.classes.Find(a => a.id == selectedClass).will;

        ClassInfo selectedClassObj = db2.classes.Find(a => a.id == selectedClass); //make a Class object
        string boost = selectedClassObj.attributeBoost; //get that classes's boost (classes only have one)
        ClearClassContributions();
        ApplyClassBoosts(boost); //pass in 
        RefreshAttributeFields();
    }
 
    void PopulateClassFeatButtons(string className)
    {
        classFeatsRadioButtonGroup.Clear(); //clear out past buttons

        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        foreach (string classFeat in selectedClass.classFeat)
        {
            var rb = new RadioButton
            {
                text = classFeat
            };
            classFeatsRadioButtonGroup.Add(rb);
        }

        classFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }

    void PopulateSubclassButtons(string className)
    {
        subclassRadioButtonGroup.Clear(); //clear out past buttons

        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        foreach (string subclass in selectedClass.subclass)
        {
            var rb = new RadioButton
            {
                text = subclass
            };
            subclassRadioButtonGroup.Add(rb);
        }

        subclassRadioButtonGroup.value = 0; // optional: auto-select first
    }

    void PopulateClassDescription(string className)
    {
        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        classDescriptionLabel.text = "Description: " + selectedClass.description;

        classBoostsLabel.text = "Attribute Boosts: " + string.Join(", ", selectedClass.attributeBoost);
        classSkillsLabel.text = "Class Skills: " + string.Join(", ", selectedClass.skills);
    }

    //for handling individual toggle changes
    private void HandleToggleChanged(Toggle changedToggle, bool isOn)
    {
        int index = toggles.IndexOf(changedToggle); //so we know the attribute associated with the toggle
        string attribute = attributeKeysForToggles[index]; //turn the index into the actual string

        if (isOn) //if toggle is on
        {
            if (selectedAttributes.Count >= maxSelections) //and the user has selected 4 already
            {
                changedToggle.SetValueWithoutNotify(false); //then prevent the next toggle from turning on
                return;
            }

            //selectedCount++; //otherwise, continue counting
            selectedAttributes.Add(attribute);
            attributes[attribute].freeChoice += 1; //for that dictionary called attributes, update the freeChoice storage
        }
        else
        {
            //selectedCount--; //if toggle is turned off, then decrease the count
            selectedAttributes.Remove(attribute);
            attributes[attribute].freeChoice -= 1;
        }

        RefreshAttributeFields();
        UpdateToggleStates();
    }

    //for handling the toggles as a group
    private void UpdateToggleStates()
    {
        bool atLimit = selectedAttributes.Count >= maxSelections; //maxed out?

        //then for each toggle, disable unchecked toggles if at limit, enable all toggles otherwise
        foreach (var toggle in toggles)
        {
            if (!toggle.value)
                toggle.SetEnabled(!atLimit);
        }
    }

    // void ClearAllAttributeToggles() //for resetting toggles. Do I need?
    // {
    //     foreach (var attribute in selectedAttributes)
    //     {
    //         attributes[attribute].freeChoice -= 1;
    //     }

    //     selectedAttributes.Clear();

    //     foreach (var toggle in toggles)
    //         toggle.SetValueWithoutNotify(false);

    //     RefreshAttributeFields();
    //     UpdateToggleStates();
    // }

    //attribute tracker helpers (3 parts): clear and apply for each category, then refresh display fields
    void ClearAncestryContributions() //anything ancestry is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.ancestry = 0;
        }
    }
    void ApplyAncestryBoosts(List<string> boosts) //pass in what boosts from ancestry to update
    {
        foreach (string boost in boosts)
        {
            attributes[boost].ancestry++;
        }
    }
    void ClearBackgroundContributions() //anything background is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.background = 0;
        }
    }
    void ApplyBackgroundBoosts(List<string> boosts) //pass in what boosts from background to update
    {
        foreach (string boost in boosts)
        {
            attributes[boost].background++;
        }
    }
    void ClearClassContributions() //anything class is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.className = 0;
        }
    }
    void ApplyClassBoosts(string boost) //pass in what boost from class to update; only ever one for classes
    {
        attributes[boost].className++;
    }
    void RefreshAttributeFields() //lastly, update the display fields
    {
        strengthAttributeField.value     = attributes["strength"].attributeTotal;
        dexterityAttributeField.value    = attributes["dexterity"].attributeTotal;
        constitutionAttributeField.value = attributes["constitution"].attributeTotal;
        intelligenceAttributeField.value = attributes["intelligence"].attributeTotal;
        wisdomAttributeField.value       = attributes["wisdom"].attributeTotal;
        charismaAttributeField.value     = attributes["charisma"].attributeTotal;
    }
}
