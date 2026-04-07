using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

//script for managing a step-by-step tutorial system
//originally designed for character creation scene
public class TutorialManager //note that this is not MonoBehaviour!
{
    private VisualElement root;
    private VisualElement overlay;
    private VisualElement panel;
    private VisualElement highlight;
    private Label textLabel;
    private Button nextButton;
    private Button skipButton;

    private List<TutorialStep> steps = new List<TutorialStep>();
    private int currentStep = 0;

    public TutorialManager(VisualElement rootElement)
    {
        root = rootElement;
        CreateUI();
    }

    private void CreateUI()
    {
        //overlay: full screen semi-transparent background
        overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.top = 0;
        overlay.style.right = 0;
        overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(0, 0, 0, 0.5f);
        overlay.style.display = DisplayStyle.None;
        overlay.pickingMode = PickingMode.Position; //this makes sure the overlay blocks clicks to underlying UI

        //panel: actual tutorial box with text and buttons
        panel = new VisualElement();
        panel.style.position = Position.Absolute;
        panel.style.backgroundColor = Color.white;
        panel.style.paddingLeft = 10;
        panel.style.paddingRight = 10;
        panel.style.paddingTop = 10;
        panel.style.paddingBottom = 10;
        panel.style.borderBottomLeftRadius = 5;
        panel.style.borderBottomRightRadius = 5;
        panel.style.borderTopLeftRadius = 5;
        panel.style.borderTopRightRadius = 5;

        //"spotlight" highlight around the target element
        highlight = new VisualElement();
        highlight.style.position = Position.Absolute;
        highlight.style.borderTopWidth = 2;
        highlight.style.borderBottomWidth = 2;
        highlight.style.borderLeftWidth = 2;
        highlight.style.borderRightWidth = 2;
        highlight.style.borderTopColor = Color.yellow;
        highlight.style.borderBottomColor = Color.yellow;
        highlight.style.borderLeftColor = Color.yellow;
        highlight.style.borderRightColor = Color.yellow;
        highlight.style.backgroundColor = new Color(1, 1, 0, 0.1f);

        textLabel = new Label();

        nextButton = new Button(NextStep) { text = "Next" };
        skipButton = new Button(EndTutorial) { text = "Skip" };

        panel.Add(textLabel);
        panel.Add(nextButton);
        panel.Add(skipButton);
        overlay.Add(panel);
        overlay.Add(highlight);
        root.Add(overlay);
    }

    //two "API" methods: lets you add steps to the tutorial and start it
    public void AddStep(VisualElement target, string message)
    {
        steps.Add(new TutorialStep { target = target, message = message });
    }

    public void StartTutorial()
    {
        if (steps.Count == 0)
            return;

        currentStep = 0;
        overlay.style.display = DisplayStyle.Flex;
        ShowStep();
    }

    //actually shows current step
    //with some fancy bits to keep the tutorial panel from going off screen
    private void ShowStep()
    {
        var step = steps[currentStep];

        root.schedule.Execute(() =>
        {
            var bounds = step.target.worldBound;
            var rootBounds = root.worldBound;

            float panelWidth = panel.resolvedStyle.width;
            float panelHeight = panel.resolvedStyle.height;

            float x = bounds.xMax + 10; //default: right side
            float y = bounds.yMin;

            //flip horizontally if overflowing right
            if (x + panelWidth > rootBounds.width)
            {
                x = bounds.xMin - panelWidth - 10; //move to left side
            }

            //flip vertically if overflowing bottom
            if (y + panelHeight > rootBounds.height)
            {
                y = rootBounds.height - panelHeight - 10;
            }

            //clamp to screen bounds just in case
            x = Mathf.Clamp(x, 10, rootBounds.width - panelWidth - 10);
            y = Mathf.Clamp(y, 10, rootBounds.height - panelHeight - 10);

            panel.style.left = x;
            panel.style.top = y;

            highlight.style.left = bounds.xMin - 4;
            highlight.style.top = bounds.yMin - 4;
            highlight.style.width = bounds.width + 8;
            highlight.style.height = bounds.height + 8;

            textLabel.text = step.message;
        });
    }

    private void NextStep()
    {
        currentStep++;

        if (currentStep >= steps.Count)
        {
            EndTutorial();
            return;
        }

        ShowStep();
    }

    private void EndTutorial()
    {
        overlay.style.display = DisplayStyle.None;
    }

    //defines a tutorial step: made up of a visual element target and the tutorial message to show
    private class TutorialStep
    {
        public VisualElement target;
        public string message;
    }
}