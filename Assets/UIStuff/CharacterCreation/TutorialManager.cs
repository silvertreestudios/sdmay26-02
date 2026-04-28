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

    public int StepCount => steps.Count;
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
        panel.style.maxWidth = 300;
        panel.style.maxHeight = 200; //maxes keep the panel from stretching off screen with long text

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
        textLabel.style.whiteSpace = WhiteSpace.Normal;
        textLabel.style.flexWrap = Wrap.Wrap; //allows text to wrap within the panel, helpful for long text also

        nextButton = new Button(NextStep) { text = "Next", name = "NextTutorialButton" };
        skipButton = new Button(EndTutorial) { text = "Skip", name = "SkipTutorialButton" };

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

            float padding = 10f;

            //make highlight rectangle
            Rect highlightRect = new Rect(
                bounds.xMin - 4,
                bounds.yMin - 4,
                bounds.width + 8,
                bounds.height + 8
            );

            //"candidate positions" (options to prevent the tutorial box from going off screen or overlapping the highlight)
            List<Vector2> candidates = new List<Vector2>()
            {
                //right
                new Vector2(bounds.xMax + padding, bounds.yMin),

                //left
                new Vector2(bounds.xMin - panelWidth - padding, bounds.yMin),

                //below
                new Vector2(bounds.xMin, bounds.yMax + padding),

                //above
                new Vector2(bounds.xMin, bounds.yMin - panelHeight - padding)
            };

            Vector2 chosen = candidates[0]; //fallback in case all candidates are bad

            //pick the first candidate that doesn't overlap the highlight and is fully on screen
            foreach (var pos in candidates)
            {
                Rect panelRect = new Rect(pos.x, pos.y, panelWidth, panelHeight);

                bool overlapsHighlight = panelRect.Overlaps(highlightRect);

                bool insideScreen =
                    pos.x >= 0 &&
                    pos.y >= 0 &&
                    pos.x + panelWidth <= rootBounds.width &&
                    pos.y + panelHeight <= rootBounds.height;

                if (!overlapsHighlight && insideScreen)
                {
                    chosen = pos;
                    break;
                }
            }

            //clamp to screen just in case
            float x = Mathf.Clamp(chosen.x, padding, rootBounds.width - panelWidth - padding);
            float y = Mathf.Clamp(chosen.y, padding, rootBounds.height - panelHeight - padding);

            panel.style.left = x;
            panel.style.top = y;

            //highlight positioning
            highlight.style.left = highlightRect.x;
            highlight.style.top = highlightRect.y;
            highlight.style.width = highlightRect.width;
            highlight.style.height = highlightRect.height;

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