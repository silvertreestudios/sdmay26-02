using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class CombatLog : CombatLogInterface
{
    protected class CombatLogMessage
    {
        public HashSet<string> Tags;
        public string Message;
        public CombatLogEntry Entry;

        public CombatLogMessage(string message) : this(CombatLogEntry.FromMessage(message), new HashSet<string>()) { }
        public CombatLogMessage(string message, HashSet<string> tags) : this(CombatLogEntry.FromMessage(message, tags), tags) { }

        public CombatLogMessage(CombatLogEntry entry, HashSet<string> tags)
        {
            Entry = entry ?? CombatLogEntry.FromMessage(string.Empty);
            Tags = tags ?? new HashSet<string>();
            Message = CombatLogEntryFormatter.ToPlainText(Entry);
        }
    }

    public VisualTreeAsset CombatLogTemplate;
    protected ListView LogList;
    protected List<CombatLogEntry> CurrentEntries;
    protected List<CombatLogMessage> Messages = new();
    protected HashSet<string> WhiteListTags = new();
    protected HashSet<string> BlackListTags = new();

    protected override void Awake()
    {
        base.Awake();
        Ui = GetComponent<UIDocument>().rootVisualElement;
    }

    void OnEnable()
    {
        VisualElement logHolder = Ui.Q<VisualElement>("CombatLog");
        var logList = CombatLogTemplate.Instantiate();
        logHolder.Add(logList);
        LogList = logList.Q<ListView>("CombatLog");

        VisualElement resizeHandle = logHolder.Q<VisualElement>("ResizeHandle");
        if (resizeHandle != null) { logHolder.Remove(resizeHandle); logHolder.Add(resizeHandle); }

        CurrentEntries = new List<CombatLogEntry>();

        LogList.makeItem = MakeLogEntryElement;
        LogList.bindItem = BindLogEntryElement;
        LogList.itemsSource = CurrentEntries;
        LogList.selectionType = SelectionType.None;
    }

    protected void RefreshLogList()
    {
        CurrentEntries = GetVisibleEntries();
        LogList.itemsSource = CurrentEntries;
        LogList.Rebuild();
        if (LogList != null && CurrentEntries.Count > 0)
            LogList.ScrollToItem(CurrentEntries.Count - 1);
    }

    protected void UpdateLogList(CombatLogEntry entry)
    {
        CurrentEntries.Add(entry);
        LogList.RefreshItems();
        if (LogList != null)
            LogList.ScrollToItem(CurrentEntries.Count - 1);
    }

    [ContextMenu("Enable Dev Mode")]
    public override void DevMode()
    {
        WhiteListTags.Remove("dev");
        BlackListTags.Remove("dev");
        RefreshLogList();
    }

    [ContextMenu("Disable Dev Mode")]
    public override void ReleaseMode()
    {
        WhiteListTags.Remove("dev");
        BlackListTags.Add("dev");
        RefreshLogList();
    }

    public override void DevLog(string msg)
    {
        CombatLogMessage message = new CombatLogMessage(msg, new HashSet<string> { "dev" });
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void DevLog(string msg, string tag)
    {
        CombatLogMessage message = new CombatLogMessage(msg, new HashSet<string> { "dev", tag.ToLower() });
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void DevLog(string msg, List<string> tags)
    {
        HashSet<string> t = NormalizeTags(tags);
        t.Add("dev");
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void Log(string msg)
    {
        CombatLogMessage message = new CombatLogMessage(msg, new HashSet<string>());
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void Log(string msg, string tag)
    {
        HashSet<string> t = new HashSet<string> { tag.ToLower() };
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void Log(string msg, List<string> tags)
    {
        HashSet<string> t = NormalizeTags(tags);
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override void LogEntry(CombatLogEntry entry)
    {
        HashSet<string> tags = new HashSet<string>();
        if (entry?.Tags != null)
        {
            foreach (string tag in entry.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag.ToLower());
            }
        }

        CombatLogMessage message = new CombatLogMessage(entry, tags);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(message.Entry);
    }

    public override List<string> GetMessages()
    {
        List<string> msgs = new();
        foreach (CombatLogMessage msg in Messages)
        {
            if (Filter(msg))
                msgs.Add(msg.Message);
        }
        return msgs;
    }

    public override void AddWhiteList(string tag)
    {
        WhiteListTags.Add(tag.ToLower());
        RefreshLogList();
    }

    public override void AddBlackList(string tag)
    {
        BlackListTags.Add(tag.ToLower());
        RefreshLogList();
    }

    protected bool Filter(CombatLogMessage msg)
    {
        foreach (string tag in WhiteListTags)
        {
            if (!msg.Tags.Contains(tag))
                return false;
        }
        foreach (string tag in BlackListTags)
        {
            if (msg.Tags.Contains(tag))
                return false;
        }
        return true;
    }

    private List<CombatLogEntry> GetVisibleEntries()
    {
        List<CombatLogEntry> entries = new();
        foreach (CombatLogMessage msg in Messages)
        {
            if (Filter(msg))
                entries.Add(msg.Entry);
        }
        return entries;
    }

    private static HashSet<string> NormalizeTags(IEnumerable<string> tags)
    {
        HashSet<string> normalized = new();
        if (tags == null)
            return normalized;

        foreach (string tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                normalized.Add(tag.ToLower());
        }
        return normalized;
    }

    private VisualElement MakeLogEntryElement()
    {
        VisualElement root = new VisualElement();
        root.AddToClassList("combat-log-entry");

        VisualElement accent = new VisualElement();
        accent.AddToClassList("combat-log-entry__accent");
        root.Add(accent);

        VisualElement body = new VisualElement();
        body.AddToClassList("combat-log-entry__body");
        root.Add(body);

        VisualElement line = new VisualElement();
        line.AddToClassList("combat-log-entry__line");
        body.Add(line);

        Label summary = new Label { name = "Summary" };
        summary.AddToClassList("combat-log-entry__summary");
        line.Add(summary);

        Label roll = new Label { name = "RollChip" };
        roll.AddToClassList("combat-log-entry__chip");
        line.Add(roll);

        Label damage = new Label { name = "DamageChip" };
        damage.AddToClassList("combat-log-entry__chip");
        line.Add(damage);

        VisualElement details = new VisualElement { name = "Details" };
        details.AddToClassList("combat-log-entry__details");
        body.Add(details);

        root.RegisterCallback<ClickEvent>(_ =>
        {
            if (root.userData is not CombatLogEntry entry || entry.Details.Count == 0)
                return;

            entry.Expanded = !entry.Expanded;
            LogList?.RefreshItems();
        });

        return root;
    }

    private void BindLogEntryElement(VisualElement root, int index)
    {
        CombatLogEntry entry = CurrentEntries[index];
        root.userData = entry;
        root.EnableInClassList("combat-log-entry--attack", entry.Kind == CombatLogEntryKind.Attack);
        root.EnableInClassList("combat-log-entry--expanded", entry.Expanded);
        SetOutcomeClass(root, entry.Outcome);

        Label summary = root.Q<Label>("Summary");
        summary.text = CombatLogEntryFormatter.ToSummary(entry);

        Label roll = root.Q<Label>("RollChip");
        roll.text = entry.Roll?.Summary ?? CombatLogEntryFormatter.FormatOutcome(entry.Outcome);
        roll.style.display = string.IsNullOrWhiteSpace(roll.text) ? DisplayStyle.None : DisplayStyle.Flex;

        Label damage = root.Q<Label>("DamageChip");
        damage.text = entry.Damage?.Summary ?? string.Empty;
        damage.style.display = string.IsNullOrWhiteSpace(damage.text) ? DisplayStyle.None : DisplayStyle.Flex;

        VisualElement details = root.Q<VisualElement>("Details");
        details.Clear();
        details.style.display = entry.Expanded && entry.Details.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (CombatLogDetail detail in entry.Details)
        {
            Label detailLabel = new Label(detail.Label + ": " + detail.Value);
            detailLabel.AddToClassList("combat-log-entry__detail");
            details.Add(detailLabel);
        }
    }

    private static void SetOutcomeClass(VisualElement root, CombatLogOutcome outcome)
    {
        root.EnableInClassList("combat-log-entry--hit", outcome == CombatLogOutcome.Success);
        root.EnableInClassList("combat-log-entry--crit", outcome == CombatLogOutcome.CriticalSuccess);
        root.EnableInClassList("combat-log-entry--miss", outcome == CombatLogOutcome.Failure || outcome == CombatLogOutcome.CriticalFailure);
        root.EnableInClassList("combat-log-entry--damage", outcome == CombatLogOutcome.Damage);
        root.EnableInClassList("combat-log-entry--system", outcome == CombatLogOutcome.System || outcome == CombatLogOutcome.None);
    }
}