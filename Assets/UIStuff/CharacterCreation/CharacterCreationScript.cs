using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

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
    public Defenses defenses; //defense name -> proficieny level
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

//because I made defenses nested in the json
[System.Serializable]
public class Defenses
{
    public string unarmored;
    public string lightArmor;
    public string mediumArmor;
    public string allArmor;
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
    public string size;
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
    public string simpleWeapons;
    public string martialWeapons;
    public string advancedWeapons;
    public string unarmedAttack;
    public string unarmored;
    public string lightArmor;
    public string mediumArmor;
    public string allArmor;
    public string ancestryFeat;
    public string classFeat;
    public string subclass;
    public string[] specialAbilities;

    //hard coded for barbarian test
    public string weapon = "Great Axe";
    public string armor = "Scalemail";
}

//individually track the attribute boosts from ancestry, background, and class. Not json related
[System.Serializable]
public class AttributeContributions
{
    public int ancestry;
    public int ancestryFreeChoice;
    public int background;
    public int backgroundFreeChoice;
    public int className;
    public int freeChoice;
    public int attributeTotal =>
        ancestry + ancestryFreeChoice + background + backgroundFreeChoice + className + freeChoice;
}

//Inherits from class `MonoBehaviour`. This makes it attachable to a game object as a component.
public class CharacterCreationScript : MonoBehaviour
{
    [SerializeField]
    private ViewModel characterClassModel; //this is the spinning model in the middle (refer to ViewModel.cs)
    public TutorialManager tutorial { get; private set; }
    RadioButtonGroup ancestryRadioButtonGroup;
    RadioButtonGroup heritageRadioButtonGroup;
    RadioButtonGroup ancestryFeatsRadioButtonGroup;
    RadioButtonGroup ancestryFreeBoostRadioButtonGroup;
    RadioButtonGroup backgroundRadioButtonGroup;
    RadioButtonGroup backgroundBoostChoiceRadioButtonGroup;
    RadioButtonGroup backgroundFreeBoostRadioButtonGroup;
    RadioButtonGroup classFeatsRadioButtonGroup;
    RadioButtonGroup classesRadioButtonGroup;
    RadioButtonGroup subclassRadioButtonGroup;
    RadioButtonGroup genderRadioButtonGroup;
    Label backgroundDescriptionLabel;
    Label backgroundSkillLabel;
    Label backgroundSkillFeatLabel;
    Label ancestryDescriptionLabel;
    Label ancestryBoostsFlawsLabel;
    Label ancestrySpecialAbilitiesLabel;
    Label classDescriptionLabel;
    Label classBoostsLabel;
    Label classSkillsLabel;
    Label tooltip; //made dynamically!
    Button notificationElement;
    TextField ancestryChoiceField;
    TextField heritageChoiceField;
    TextField backgroundChoiceField;
    TextField classChoiceField;
    Label hpField;
    Label speedField;
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
    TextField nameField;
    Label sizeField;
    TextField subclassField;
    TextField ancestryFeatField;
    TextField classFeatField;
    Toggle strengthToggle;
    Toggle dexterityToggle;
    Toggle constitutionToggle;
    Toggle intelligenceToggle;
    Toggle wisdomToggle;
    Toggle charismaToggle;
    Button jsonDebug;
    Button defaultBarbarian;
    Button finishCharacterCreation;
    Tab ancestryTab;
    Tab backgroundTab;
    Tab classTab;
    Tab finalBoostsTab;
    VisualElement ancestryTabHeader;
    VisualElement backgroundTabHeader;
    VisualElement classTabHeader;
    VisualElement finalBoostsTabHeader;
    VisualElement leftInfoPanel;
    Foldout attackDropdownMenu;
    Foldout defenseDropdownMenu;
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
        ancestryFreeBoostRadioButtonGroup = root.Q<RadioButtonGroup>("FreeBoostRadioButtonGroup");
        backgroundRadioButtonGroup = root.Q<RadioButtonGroup>("BackgroundRadioButtonGroup");
        backgroundBoostChoiceRadioButtonGroup = root.Q<RadioButtonGroup>(
            "BackgroundBoostChoiceRadioButtonGroup"
        );
        backgroundFreeBoostRadioButtonGroup = root.Q<RadioButtonGroup>(
            "BackgroundFreeBoostRadioGroup"
        );
        backgroundDescriptionLabel = root.Q<Label>("BackgroundDescriptionLabel");
        backgroundSkillLabel = root.Q<Label>("BackgroundSkillLabel");
        backgroundSkillFeatLabel = root.Q<Label>("BackgroundSkillFeatLabel");
        classFeatsRadioButtonGroup = root.Q<RadioButtonGroup>("ClassFeatsRadioButtonGroup");
        classesRadioButtonGroup = root.Q<RadioButtonGroup>("ClassesRadioButtonGroup");
        genderRadioButtonGroup = root.Q<RadioButtonGroup>("Gender");
        ancestryDescriptionLabel = root.Q<Label>("AncestryDescription");
        ancestryBoostsFlawsLabel = root.Q<Label>("AncestryBoostsFlaws");
        ancestrySpecialAbilitiesLabel = root.Q<Label>("AncestrySpecialAbilities");
        ancestryChoiceField = root.Q<TextField>("AncestryChoice");
        heritageChoiceField = root.Q<TextField>("HeritageChoice");
        hpField = root.Q<Label>("HP");
        speedField = root.Q<Label>("Speed");
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
        jsonDebug = root.Q<Button>("jsondebug");
        defaultBarbarian = root.Q<Button>("DefaultBarbarianButton");
        finishCharacterCreation = root.Q<Button>("FinishCharacterCreationButton");
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
        nameField = root.Q<TextField>("NameField");
        sizeField = root.Q<Label>("Size");
        subclassField = root.Q<TextField>("Subclass");
        ancestryFeatField = root.Q<TextField>("AncestryFeatField");
        classFeatField = root.Q<TextField>("ClassFeatField");
        notificationElement = root.Q<Button>("NotificationElement");
        ancestryTab = root.Q<Tab>("AncestryTab");
        ancestryTabHeader = ancestryTab.tabHeader;
        backgroundTab = root.Q<Tab>("BackgroundTab");
        backgroundTabHeader = backgroundTab.tabHeader;
        classTab = root.Q<Tab>("ClassTab");
        classTabHeader = classTab.tabHeader;
        finalBoostsTab = root.Q<Tab>("FinalBoosts");
        finalBoostsTabHeader = finalBoostsTab.tabHeader;
        leftInfoPanel = root.Q<VisualElement>("LeftPanel");
        attackDropdownMenu = root.Q<Foldout>("ClassAttackFoldout");
        defenseDropdownMenu = root.Q<Foldout>("ClassDefenseFoldout");

        //for json, assigning
        jsonFile = Resources.Load<TextAsset>("Data/ancestry");
        db = JsonUtility.FromJson<AncestryDatabase>(jsonFile.text);

        jsonFile2 = Resources.Load<TextAsset>("Data/class");
        db2 = JsonUtility.FromJson<ClassDatabase>(jsonFile2.text);

        //PLAYERCHARACTER JSON
        currentCharacter = new PlayerCharacter();
        //currentCharacter.ancestry = "elf";
        jsonFile3 = JsonUtility.ToJson(currentCharacter);

        characterClassModel = FindFirstObjectByType<ViewModel>(); //instanciate to the ViewModel in the scene

        //small enough that I'm keeping as a dictionary for now
        backgroundDescriptionByBackground = new Dictionary<string, List<string>>()
        {
            {
                "Acolyte",
                new List<string>
                {
                    "You spent your early days in a religious monastery or cloister. You may have traveled out into the world to spread the message of your religion or because you cast away the teachings of your faith, but deep down you'll always carry within you the lessons you learned.",
                    "Intelligence",
                    "Wisdom",
                    "Religion",
                    "Student of the Canon",
                }
            },
            {
                "Bandit",
                new List<string>
                {
                    "Your past includes no small amount of rural banditry, robbing travelers on the road and scraping by. Whether your robbery was sanctioned by a local noble or you did so of your own accord, you eventually got caught up in the adventuring life. Now, adventure is your stock and trade, and years of camping and skirmishing have only helped.",
                    "Dexterity",
                    "Charisma",
                    "Intimidation",
                    "Group Coercion",
                }
            },
            {
                "Cook",
                new List<string>
                {
                    "You grew up in the kitchens of a tavern or other dining establishment and excelled there, becoming an exceptional cook. Baking, cooking, a little brewing on the side—you've spent lots of time out of sight. It's about time you went out into the world to catch some sights for yourself",
                    "Constitution",
                    "Intelligence",
                    "Survival",
                    "Seasoned",
                }
            },
        };

        //initialize "attributes" dictionary with all 6 attributes, each with an AttributeContributions object
        attributes = new Dictionary<string, AttributeContributions>();
        attributes.Add("Strength", new AttributeContributions());
        attributes.Add("Dexterity", new AttributeContributions());
        attributes.Add("Constitution", new AttributeContributions());
        attributes.Add("Intelligence", new AttributeContributions());
        attributes.Add("Wisdom", new AttributeContributions());
        attributes.Add("Charisma", new AttributeContributions());

        jsonDebug.clicked += PrintJson; //FOR DEBUGGING THE PLAYERCHARACTER JSON
        defaultBarbarian.clicked += PopulateDefaultBarbarianJsonAndUI;
        finishCharacterCreation.clicked += FinishCreation;

        CreateTooltip(root);

        //TESTING. Would be cleaner as a separate function...
        tutorial = new TutorialManager(root);
        tutorial.AddStep(
            root,
            "Welcome to character creation! Click 'Next' for a short tutorial or 'Skip' to get straight to building your hero."
        );
        tutorial.AddStep(
            ancestryTabHeader,
            "Start by choosing your ancestry, which grants innate traits."
        );
        tutorial.AddStep(
            ancestryTab,
            "Your ancestry determines available heritages and ancestry feats. Don't forget to select a free attribute boost, too."
        );
        tutorial.AddStep(
            backgroundTabHeader,
            "Next, choose your background, which reflects your character's past and grants additional training."
        );
        tutorial.AddStep(
            classTabHeader,
            "Now select your class, which defines your core abilities and combat role. You'll also choose a subclass and class feats."
        );
        tutorial.AddStep(
            finalBoostsTabHeader,
            "Assign your remaining attribute boosts. You may choose four total, so plan carefully."
        );
        tutorial.AddStep(nameField, "Give your character a name to complete their identity.");
        tutorial.AddStep(
            leftInfoPanel,
            "Review your character details here. Hover over options for more information."
        );
        tutorial.AddStep(
            finishCharacterCreation,
            "When you're ready, finalize your character and begin your adventure!"
        );
        tutorial.AddStep(
            defaultBarbarian,
            "Short on time? Use a preset barbarian build to jump straight into the game."
        );
        tutorial.StartTutorial();

        nameField.RegisterValueChangedCallback(OnNameChanged);
        genderRadioButtonGroup.RegisterValueChangedCallback(OnGenderChanged);

        ancestryRadioButtonGroup.RegisterValueChangedCallback(OnAncestryChanged);
        ancestryFeatsRadioButtonGroup.RegisterValueChangedCallback(OnAncestryFeatChanged);
        ancestryFreeBoostRadioButtonGroup.RegisterValueChangedCallback(OnAncestryFreeBoostChanged);
        backgroundRadioButtonGroup.RegisterValueChangedCallback(OnBackgroundChanged);
        classesRadioButtonGroup.RegisterValueChangedCallback(OnClassChanged);
        classFeatsRadioButtonGroup.RegisterValueChangedCallback(OnClassFeatChanged);
        subclassRadioButtonGroup.RegisterValueChangedCallback(OnSubclassChanged);
        heritageRadioButtonGroup.RegisterValueChangedCallback(OnHeritageChanged);
        backgroundBoostChoiceRadioButtonGroup.RegisterValueChangedCallback(
            OnBackgroundBoostChanged
        );
        backgroundFreeBoostRadioButtonGroup.RegisterValueChangedCallback(
            OnBackgroundFreeBoostChanged
        );

        //no special grouping for toggles, so I'm handling them as list manually
        selectedAttributes = new();
        toggles = new List<Toggle>
        {
            strengthToggle,
            dexterityToggle,
            constitutionToggle,
            intelligenceToggle,
            wisdomToggle,
            charismaToggle,
        };
        attributeKeysForToggles = new List<string>
        {
            "Strength",
            "Dexterity",
            "Constitution",
            "Intelligence",
            "Wisdom",
            "Charisma",
        };
        foreach (var toggle in toggles)
        {
            toggle.RegisterValueChangedCallback(evt =>
            {
                HandleToggleChanged(toggle, evt.newValue);
            });
        }
    }

    //Continuously called. OnEnable only happens once
    void Update()
    {
        //update tooltip text for hpField in case classHP or ancestryHP has changed
        HoverOverElement(
            hpField,
            "Health \nHP from class: " + classHP + "\nHP from ancestry: " + ancestryHP
        );
        HoverOverElement(
            strengthAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Strength"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Strength"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Strength"].background
                + "\nBackground Free Choice: "
                + attributes["Strength"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Strength"].className
                + "\nFree Choice: "
                + attributes["Strength"].freeChoice
        );
        HoverOverElement(
            dexterityAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Dexterity"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Dexterity"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Dexterity"].background
                + "\nBackground Free Choice: "
                + attributes["Dexterity"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Dexterity"].className
                + "\nFree Choice: "
                + attributes["Dexterity"].freeChoice
        );
        HoverOverElement(
            constitutionAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Constitution"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Constitution"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Constitution"].background
                + "\nBackground Free Choice: "
                + attributes["Constitution"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Constitution"].className
                + "\nFree Choice: "
                + attributes["Constitution"].freeChoice
        );
        HoverOverElement(
            intelligenceAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Intelligence"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Intelligence"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Intelligence"].background
                + "\nBackground Free Choice: "
                + attributes["Intelligence"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Intelligence"].className
                + "\nFree Choice: "
                + attributes["Intelligence"].freeChoice
        );
        HoverOverElement(
            wisdomAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Wisdom"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Wisdom"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Wisdom"].background
                + "\nBackground Free Choice: "
                + attributes["Wisdom"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Wisdom"].className
                + "\nFree Choice: "
                + attributes["Wisdom"].freeChoice
        );
        HoverOverElement(
            charismaAttributeField,
            "Breakdown:\nAncestry: "
                + attributes["Charisma"].ancestry
                + "\nAncestry Free Choice: "
                + attributes["Charisma"].ancestryFreeChoice
                + "\nBackground: "
                + attributes["Charisma"].background
                + "\nBackground Free Choice: "
                + attributes["Charisma"].backgroundFreeChoice
                + "\nClass: "
                + attributes["Charisma"].className
                + "\nFree Choice: "
                + attributes["Charisma"].freeChoice
        );

        HoverOverElement(sizeField, "Size is determined by ancestry. May be Small or Medium.");
        HoverOverElement(
            speedField,
            "Speed is determined by ancestry. It is how far you can move in one action."
        );
        HoverOverElement(
            perceptionField,
            "Perception is a measure of how aware your character is of their surroundings. It is determined by class."
        );
        HoverOverElement(
            fortitudeField,
            "Fortitude is a measure of your character's physical toughness and resilience. It is determined by class."
        );
        HoverOverElement(
            reflexField,
            "Reflex is a measure of your character's agility and quickness. It is determined by class."
        );
        HoverOverElement(
            willField,
            "Will is a measure of your character's mental fortitude and determination. It is determined by class."
        );
        HoverOverElement(
            attackDropdownMenu,
            "Proficiency with various weapon types. Determined by class."
        );
        HoverOverElement(
            defenseDropdownMenu,
            "Proficiency with different armor types and unarmored defense. Determined by class."
        );

        HoverOverElement(
            notificationElement,
            "You are missing required fields. Click this message to dismiss."
        );
    }

    void PrintJson()
    {
        Debug.Log(JsonUtility.ToJson(currentCharacter, true));
    }

    void PopulateDefaultBarbarianJsonAndUI()
    {
        currentCharacter = CreateDefaultBarbarian();
        UpdateUIFromCharacter(currentCharacter);

        jsonFile3 = JsonUtility.ToJson(currentCharacter); //added this to see if refreshing the json helps, no change so can probably delete later

        Debug.Log(currentCharacter.strength + " should be 4"); //so strength is 4, but the json and display fields are not
    }

    //makes a default barbarian as a PlayerCharacter object for the json
    PlayerCharacter CreateDefaultBarbarian()
    {
        return new PlayerCharacter
        {
            name = "Torgrim",
            gender = "female",
            ancestry = "Dwarf",
            heritage = "Rock",
            background = "Bandit",
            className = "Barbarian",

            hp = 22,
            speed = 20,
            size = "medium",

            strength = 4,
            dexterity = 2,
            constitution = 1,
            intelligence = 1,
            wisdom = 1,
            charisma = 0,

            perception = "expert",
            fortitude = "expert",
            reflex = "trained",
            will = "expert",

            simpleWeapons = "trained",
            martialWeapons = "trained",
            advancedWeapons = "untrained",
            unarmedAttack = "trained",

            unarmored = "trained",
            lightArmor = "trained",
            mediumArmor = "trained",
            allArmor = "untrained",

            ancestryFeat = "Mountain Strategy",
            classFeat = "Raging Intimidation",
            subclass = "Fury Instinct",

            specialAbilities = new string[] { "dark vision", "clan dagger" },

            weapon = "Great Axe",
            armor = "Scalemail",
        };
    }

    //uses the default barbarian PlayerCharacter object to populate the UI display fields
    void UpdateUIFromCharacter(PlayerCharacter c)
    {
        characterClassModel.setMeshName("Barbarian");

        nameField.value = c.name;

        genderRadioButtonGroup.value = c.gender == "male" ? 0 : 1;

        ancestryRadioButtonGroup.SetValueWithoutNotify(2);
        ancestryChoiceField.value = c.ancestry;
        PopulateAncestryDescription(c.ancestry);
        PopulateAncestryFeatButtons(c.ancestry);

        PopulateHeritageButtons(c.ancestry);
        heritageRadioButtonGroup.SetValueWithoutNotify(3);
        heritageChoiceField.value = c.heritage;

        backgroundRadioButtonGroup.SetValueWithoutNotify(1);
        backgroundChoiceField.value = c.background;
        PopulateBackgroundInfo(c.background);

        classesRadioButtonGroup.SetValueWithoutNotify(4);
        classChoiceField.value = c.className;
        PopulateClassDescription(c.className);
        PopulateClassFeatButtons(c.className);
        PopulateSubclassButtons(c.className);

        hpField.text = c.hp.ToString();
        speedField.text = c.speed.ToString();
        sizeField.text = c.size;

        strengthAttributeField.value = c.strength;
        dexterityAttributeField.value = c.dexterity;
        constitutionAttributeField.value = c.constitution;
        intelligenceAttributeField.value = c.intelligence;
        wisdomAttributeField.value = c.wisdom;
        charismaAttributeField.value = c.charisma;

        perceptionField.value = c.perception;
        fortitudeField.value = c.fortitude;
        reflexField.value = c.reflex;
        willField.value = c.will;

        simpleWeaponsField.value = c.simpleWeapons;
        martialWeaponsField.value = c.martialWeapons;
        advancedWeaponsField.value = c.advancedWeapons;
        unarmedAttackField.value = c.unarmedAttack;

        unarmoredDefenseField.value = c.unarmored;
        lightArmorField.value = c.lightArmor;
        mediumArmorField.value = c.mediumArmor;
        allArmorField.value = c.allArmor;

        //had to use SetValueWithoutNotify for the RadioButtonGroups because setting the value was triggering an event and causing errors
        ancestryFeatsRadioButtonGroup.SetValueWithoutNotify(2);
        classFeatsRadioButtonGroup.SetValueWithoutNotify(4);
        subclassRadioButtonGroup.SetValueWithoutNotify(2);
        subclassField.value = c.subclass;
    }

    void FinishCreation()
    {
        //no need to show overview because everything is displayed already

        bool ready = false;

        //check json/currentCharacter object for any null fields
        ready = HasNullFields(currentCharacter);

        //check attributes. There's gotta be a way to make this cleaner. What if multiple are over 5? I think it does just once at a time
        if (currentCharacter.dexterity >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Dexterity cannot be more than 4";
            notificationElement.clicked += Disappear;
        }
        if (currentCharacter.charisma >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Charisma cannot be more than 4";
            notificationElement.clicked += Disappear;
        }
        if (currentCharacter.strength >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Strength cannot be more than 4";
            notificationElement.clicked += Disappear;
        }
        if (currentCharacter.wisdom >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Wisdom cannot be more than 4";
            notificationElement.clicked += Disappear;
        }
        if (currentCharacter.intelligence >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Intelligence cannot be more than 4";
            notificationElement.clicked += Disappear;
        }
        if (currentCharacter.constitution >= 5)
        {
            notificationElement.style.display = DisplayStyle.Flex;
            notificationElement.text = "Constitution cannot be more than 4";
            notificationElement.clicked += Disappear;
        }

        //change scene to gameplay
        if (ready)
        {
            SceneTransitionManager.FadeAndLoad("Level1");
        }
    }

    //this could probably be better...
    void Disappear()
    {
        notificationElement.style.display = DisplayStyle.None;
    }

    //helper function for checking for null/unassigned variables for currentCharacter
    public bool HasNullFields(PlayerCharacter character)
    {
        foreach (FieldInfo field in typeof(PlayerCharacter).GetFields())
        {
            object value = field.GetValue(character);

            if (value == null) //if a variable is null
            {
                notificationElement.style.display = DisplayStyle.Flex;
                notificationElement.text = field.Name + " has not been assigned";
                notificationElement.clicked += Disappear;
                return false;
            }
        }

        return true;
    }

    void OnNameChanged(ChangeEvent<string> evt)
    {
        string enteredName = evt.newValue;
        currentCharacter.name = enteredName;
    }

    void OnGenderChanged(ChangeEvent<int> evt)
    {
        string selectedGender = (genderRadioButtonGroup[evt.newValue] as RadioButton).label;
        currentCharacter.gender = selectedGender;
    }

    //when there's a change in the ancestryRadioGroup, grab that text
    void OnAncestryChanged(ChangeEvent<int> evt)
    {
        ancestryHP = 0; //resets the ancestry hp every the ancestry changes so it doesn't keep adding up

        string selectedAncestry = (ancestryRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        ancestryChoiceField.value = selectedAncestry;
        currentCharacter.ancestry = selectedAncestry; //send to PlayerCharacter json
        ancestryHP = db.ancestries.Find(a => a.id == selectedAncestry).hp;
        hpField.text = (classHP + ancestryHP).ToString(); //add ancestry hp to other hp
        currentCharacter.hp = int.Parse(hpField.text);

        Ancestry selectedAncestryObj = db.ancestries.Find(a => a.id == selectedAncestry); //make an ancestry object
        List<string> boosts = new List<string>(selectedAncestryObj.attributeBoost); //make a list of that ancestry's boosts
        ClearAncestryContributions();
        ApplyAncestryBoosts(boosts); //pass in List of strings
        ApplyAncestryFlaw(db.ancestries.Find(a => a.id == selectedAncestry).attributeFlaw);
        RefreshAttributeFields();

        speedField.text = db.ancestries.Find(a => a.id == selectedAncestry).speed.ToString();
        currentCharacter.speed = int.Parse(speedField.text);
        currentCharacter.size = db.ancestries.Find(a => a.id == selectedAncestry).size;
        sizeField.text = currentCharacter.size;
        PopulateHeritageButtons(selectedAncestry);
        PopulateAncestryFeatButtons(selectedAncestry);
        PopulateAncestryDescription(selectedAncestry);
    }

    //when there's a change in the heritageRadioGroup, grab that text
    void OnHeritageChanged(ChangeEvent<int> evt)
    {
        //guards against -1, which is when no button is selected
        if (evt.newValue < 0)
        {
            return;
        }

        string selectedHeritage = (heritageRadioButtonGroup[evt.newValue] as RadioButton).text; //for some reason .label doesn't work here but .text does
        heritageChoiceField.value = selectedHeritage;
        currentCharacter.heritage = selectedHeritage;
    }

    void OnAncestryFreeBoostChanged(ChangeEvent<int> evt)
    {
        string selectedAncestryBoost = (
            ancestryFreeBoostRadioButtonGroup[evt.newValue] as RadioButton
        ).label; //evt.newValue is the index of the selected button

        //handle attributes
        ClearAncestryFreeChoiceContributions();
        ApplyAncestryFreeChoiceBoosts(selectedAncestryBoost); //pass in boost string
        RefreshAttributeFields();
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
            var rb = new RadioButton { text = heritage };

            rb.AddToClassList("pill-radio"); //add custom class for styling
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
            var rb = new RadioButton { text = ancestryFeat };
            rb.AddToClassList("pill-radio"); //add custom class for styling
            ancestryFeatsRadioButtonGroup.Add(rb);
        }

        ancestryFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }

    //when there's a change in the ancestry feats RadioGroup, grab that text
    void OnAncestryFeatChanged(ChangeEvent<int> evt)
    {
        //guards against -1, which is when no button is selected
        if (evt.newValue < 0)
        {
            return;
        }

        string selectedAncestryFeat = (
            ancestryFeatsRadioButtonGroup[evt.newValue] as RadioButton
        ).text; //for some reason .label doesn't work here but .text does
        ancestryFeatField.value = selectedAncestryFeat;
        currentCharacter.ancestryFeat = selectedAncestryFeat;
    }

    //also populates attribute boosts and flaws
    void PopulateAncestryDescription(string ancestry)
    {
        Ancestry selectedAncestry = db.ancestries.Find(a => a.id == ancestry);
        ancestryDescriptionLabel.text = "Description: " + selectedAncestry.description;

        ancestryBoostsFlawsLabel.text =
            "Attribute Boosts: "
            + string.Join(", ", selectedAncestry.attributeBoost)
            + "\n"
            + "Attribute Flaw: "
            + selectedAncestry.attributeFlaw;
        ancestrySpecialAbilitiesLabel.text =
            "Special Abilities: " + string.Join(", ", selectedAncestry.specialAbilities);
        currentCharacter.specialAbilities = selectedAncestry.specialAbilities;
    }

    //when there's a change in the backgroundRadioGroup, grab that text
    void OnBackgroundChanged(ChangeEvent<int> evt)
    {
        string selectedBackground = (backgroundRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateBackgroundInfo(selectedBackground);
        backgroundChoiceField.value = selectedBackground;

        currentCharacter.background = selectedBackground;

        //when background is changed, also reset the background boosts
        ClearBackgroundContributions();
        RefreshAttributeFields();
    }

    void PopulateBackgroundInfo(string background)
    {
        backgroundBoostChoiceRadioButtonGroup.Clear();

        if (!backgroundDescriptionByBackground.TryGetValue(background, out var backgroundBoost))
            return;

        backgroundDescriptionLabel.text =
            "Description: " + backgroundDescriptionByBackground[background][0];
        backgroundSkillLabel.text = "Skill: " + backgroundDescriptionByBackground[background][3];
        backgroundSkillFeatLabel.text =
            "Skill Feat: " + backgroundDescriptionByBackground[background][4];

        //make a new button where the text is the 1st boost
        var rb = new RadioButton
        {
            text = backgroundDescriptionByBackground[background][1], //set description text
        };
        //make a new button where the text is the 2nd boost
        var rb2 = new RadioButton { text = backgroundDescriptionByBackground[background][2] };

        rb.AddToClassList("pill-radio"); //add custom class for styling
        backgroundBoostChoiceRadioButtonGroup.Add(rb);
        rb2.AddToClassList("pill-radio"); //add custom class for styling
        backgroundBoostChoiceRadioButtonGroup.Add(rb2);
    }

    //when there's a change in the RadioGroup, grab that text
    void OnBackgroundBoostChanged(ChangeEvent<int> evt)
    {
        //guard against "no selection", similar to heritage case
        if (evt.newValue < 0)
            return;

        string selectedBackgroundBoost = (
            backgroundBoostChoiceRadioButtonGroup[evt.newValue] as RadioButton
        ).text;

        //handle attributes
        ClearBackgroundContributions();
        ApplyBackgroundBoosts(selectedBackgroundBoost); //pass in boost string
        RefreshAttributeFields();
    }

    void OnBackgroundFreeBoostChanged(ChangeEvent<int> evt)
    {
        string selectedBackgroundBoost = (
            backgroundFreeBoostRadioButtonGroup[evt.newValue] as RadioButton
        ).label; //evt.newValue is the index of the selected button

        //handle attributes
        ClearBackgroundFreeChoiceContributions();
        ApplyBackgroundFreeChoiceBoosts(selectedBackgroundBoost); //pass in boost string
        RefreshAttributeFields();
    }

    void OnClassChanged(ChangeEvent<int> evt)
    {
        string selectedClass = (classesRadioButtonGroup[evt.newValue] as RadioButton).label; //evt.newValue is the index of the selected button
        PopulateClassFeatButtons(selectedClass);
        PopulateSubclassButtons(selectedClass);
        PopulateClassDescription(selectedClass);

        classChoiceField.value = selectedClass;

        characterClassModel.setMeshName(selectedClass); //sets the spinning model according to the class
        Debug.Log("setMeshName called with: " + selectedClass);

        classHP = 0;
        classHP = db2.classes.Find(a => a.id == selectedClass).hp;
        hpField.text = (ancestryHP + classHP).ToString(); //add class hp to total hp

        currentCharacter.hp = int.Parse(hpField.text);

        simpleWeaponsField.value = db2
            .classes.Find(a => a.id == selectedClass)
            .attacks.simpleWeapons;
        martialWeaponsField.value = db2
            .classes.Find(a => a.id == selectedClass)
            .attacks.martialWeapons;
        advancedWeaponsField.value = db2
            .classes.Find(a => a.id == selectedClass)
            .attacks.advancedWeapons;
        unarmedAttackField.value = db2
            .classes.Find(a => a.id == selectedClass)
            .attacks.unarmedAttacks;

        currentCharacter.simpleWeapons = simpleWeaponsField.value;
        currentCharacter.martialWeapons = martialWeaponsField.value;
        currentCharacter.advancedWeapons = advancedWeaponsField.value;
        currentCharacter.unarmedAttack = unarmedAttackField.value;

        unarmoredDefenseField.value = db2
            .classes.Find(a => a.id == selectedClass)
            .defenses.unarmored;
        lightArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.lightArmor;
        mediumArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.mediumArmor;
        allArmorField.value = db2.classes.Find(a => a.id == selectedClass).defenses.allArmor;

        currentCharacter.unarmored = unarmoredDefenseField.value;
        currentCharacter.lightArmor = lightArmorField.value;
        currentCharacter.mediumArmor = mediumArmorField.value;
        currentCharacter.allArmor = allArmorField.value;

        perceptionField.value = db2.classes.Find(a => a.id == selectedClass).perception;
        fortitudeField.value = db2.classes.Find(a => a.id == selectedClass).fortitude;
        reflexField.value = db2.classes.Find(a => a.id == selectedClass).reflex;
        willField.value = db2.classes.Find(a => a.id == selectedClass).will;

        currentCharacter.perception = perceptionField.value;
        currentCharacter.fortitude = fortitudeField.value;
        currentCharacter.reflex = reflexField.value;
        currentCharacter.will = willField.value;

        ClassInfo selectedClassObj = db2.classes.Find(a => a.id == selectedClass); //make a Class object
        string boost = selectedClassObj.attributeBoost; //get that classes's boost (classes only have one)
        ClearClassContributions();
        ApplyClassBoosts(boost); //pass in
        RefreshAttributeFields();

        currentCharacter.className = selectedClass;
    }

    void PopulateClassFeatButtons(string className)
    {
        classFeatsRadioButtonGroup.Clear(); //clear out past buttons

        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        foreach (string classFeat in selectedClass.classFeat)
        {
            var rb = new RadioButton { text = classFeat };
            rb.AddToClassList("pill-radio"); //add custom class for styling
            classFeatsRadioButtonGroup.Add(rb);
        }

        classFeatsRadioButtonGroup.value = 0; // optional: auto-select first
    }

    //when there's a change in the class feats RadioGroup, grab that text
    void OnClassFeatChanged(ChangeEvent<int> evt)
    {
        //guards against -1, which is when no button is selected
        if (evt.newValue < 0)
        {
            return;
        }

        string selectedClassFeat = (classFeatsRadioButtonGroup[evt.newValue] as RadioButton).text; //for some reason .label doesn't work here but .text does
        classFeatField.value = selectedClassFeat;
        currentCharacter.classFeat = selectedClassFeat;
    }

    void PopulateSubclassButtons(string className)
    {
        subclassRadioButtonGroup.Clear(); //clear out past buttons

        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        foreach (string subclass in selectedClass.subclass)
        {
            var rb = new RadioButton { text = subclass };
            rb.AddToClassList("pill-radio");
            subclassRadioButtonGroup.Add(rb);
        }

        subclassRadioButtonGroup.value = 0; // optional: auto-select first
    }

    //when there's a change in the subclass RadioGroup, grab that text
    void OnSubclassChanged(ChangeEvent<int> evt)
    {
        //guards against -1, which is when no button is selected
        if (evt.newValue < 0)
        {
            return;
        }

        string selectedSubclass = (subclassRadioButtonGroup[evt.newValue] as RadioButton).text; //for some reason .label doesn't work here but .text does
        currentCharacter.subclass = selectedSubclass;
        subclassField.value = selectedSubclass;
    }

    void PopulateClassDescription(string className)
    {
        ClassInfo selectedClass = db2.classes.Find(a => a.id == className);
        classDescriptionLabel.text = "Description: " + selectedClass.description;

        classBoostsLabel.text =
            "Attribute Boosts: " + string.Join(", ", selectedClass.attributeBoost);
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

    void ApplyAncestryFlaw(string flaw) //pass in what flaw from ancestry to update
    {
        attributes[flaw].ancestry--;
    }

    void ClearAncestryFreeChoiceContributions() //ancestry free choice is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.ancestryFreeChoice = 0;
        }
    }

    void ApplyAncestryFreeChoiceBoosts(string boost) //pass in what boost from ancestry to update
    {
        attributes[boost].ancestryFreeChoice++;
    }

    void ClearBackgroundContributions() //just background is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.background = 0;
        }
    }

    void ApplyBackgroundBoosts(string boost) //pass in what boosts from background to update
    {
        attributes[boost].background++;
    }

    void ClearBackgroundFreeChoiceContributions() //background free choice is reset
    {
        foreach (var entry in attributes.Values)
        {
            entry.backgroundFreeChoice = 0;
        }
    }

    void ApplyBackgroundFreeChoiceBoosts(string boost) //pass in what boost from background to update
    {
        attributes[boost].backgroundFreeChoice++;
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
        strengthAttributeField.value = attributes["Strength"].attributeTotal;
        dexterityAttributeField.value = attributes["Dexterity"].attributeTotal;
        constitutionAttributeField.value = attributes["Constitution"].attributeTotal;
        intelligenceAttributeField.value = attributes["Intelligence"].attributeTotal;
        wisdomAttributeField.value = attributes["Wisdom"].attributeTotal;
        charismaAttributeField.value = attributes["Charisma"].attributeTotal;

        currentCharacter.strength = strengthAttributeField.value;
        currentCharacter.dexterity = dexterityAttributeField.value;
        currentCharacter.constitution = constitutionAttributeField.value;
        currentCharacter.intelligence = intelligenceAttributeField.value;
        currentCharacter.wisdom = wisdomAttributeField.value;
        currentCharacter.charisma = charismaAttributeField.value;

        // Debug.Log(JsonUtility.ToJson(currentCharacter, true));
    }

    //dynamically make Tooltip element rather than try to place it somewhere in the UXML
    void CreateTooltip(VisualElement root)
    {
        tooltip = new Label();
        tooltip.style.position = Position.Absolute;
        tooltip.style.backgroundColor = new Color(0, 0, 0, 0.8f);
        tooltip.style.color = Color.white;
        tooltip.style.paddingLeft = 5;
        tooltip.style.paddingRight = 5;
        tooltip.style.paddingTop = 3;
        tooltip.style.paddingBottom = 3;
        tooltip.style.display = DisplayStyle.None;

        root.Add(tooltip);
    }

    void HoverOverElement(VisualElement element, string tooltipText)
    {
        //on hover
        element.RegisterCallback<MouseEnterEvent>(evt =>
        {
            tooltip.text = tooltipText;
            tooltip.style.display = DisplayStyle.Flex;
        });

        //then when mouse leaves the element
        element.RegisterCallback<MouseLeaveEvent>(evt =>
        {
            tooltip.style.display = DisplayStyle.None;
        });

        //tooltip follows mouse
        element.RegisterCallback<MouseMoveEvent>(evt =>
        {
            tooltip.style.left = evt.mousePosition.x + 10;
            tooltip.style.top = evt.mousePosition.y + 10;
        });
    }
}
