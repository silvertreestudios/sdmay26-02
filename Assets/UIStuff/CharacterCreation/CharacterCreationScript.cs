using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
    
//Inherits from class `MonoBehaviour`. This makes it attachable to a game object as a component.
public class CharacterCreationScript : MonoBehaviour
{
    private void OnEnable()
    {
        UIDocument menu = GetComponent<UIDocument>();
        VisualElement root = menu.rootVisualElement;

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
}
