using Game.Strikes;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class CombatLog : CombatLogInterface
{
    protected class CombatLogMessage
    {
        public HashSet<string> Tags;
        public string Message;
        public CombatLogMessage(string message) { Message = message; Tags = new(); }
        public CombatLogMessage(string message, HashSet<string> tags) { Message = message; Tags = tags; }
    }

    public VisualTreeAsset CombatLogTemplate;
    protected ListView LogList;
    protected List<string> CurrentLogs;
    protected List<CombatLogMessage> Messages = new();
    protected HashSet<string> WhiteListTags = new();
    protected HashSet<string> BlackListTags = new();


    void Awake()
    {
        base.Awake();
        Ui = GetComponent<UIDocument>().rootVisualElement;
    }

    void OnEnable()
    {
        // Find the holder and add the defined CombatLog UI
        VisualElement logHolder = Ui.Q<VisualElement>("CombatLog");
        var logList = CombatLogTemplate.Instantiate();
        logHolder.Add(logList);
        LogList = logList.Q<ListView>("CombatLog");

        CurrentLogs = new List<string>();

        // This is a lamba expression that returns the new visual element for an item
        // Can replace new TextElement() with Prefab.Instantiate();
        Func<VisualElement> makeItem = () => new TextElement();

        // This personalizes the item from makeItem
        Action<VisualElement, int> bindItem = (e, i) =>
        {
            // Cast to TextElement, but if bindItem is a container and use bindItem.Q<Something>("Name");
            ((TextElement)e).text = CurrentLogs[i];
        };

        LogList.makeItem = makeItem;
        LogList.bindItem = bindItem;
        LogList.itemsSource = CurrentLogs;
        LogList.selectionType = SelectionType.None;
    }

    /// <summary>
    /// Expensive to refresh whole list, use UpdatelogList for small changes
    /// </summary>
    protected void RefreshLogList()
    {
        CurrentLogs = GetMessages();
        LogList.itemsSource = CurrentLogs;
        LogList.Rebuild();
        // Scroll to bottom after refresh
        if (LogList != null && CurrentLogs.Count > 0) {
            LogList.ScrollToItem(CurrentLogs.Count - 1);
        }
    }

    /// <summary>
    /// Updates the list for small changes
    /// </summary>
    /// <param name="message"></param>
    protected void UpdateLogList(string message)
    {
        CurrentLogs.Add(timestamp() + message);
        LogList.RefreshItems();
        // Scroll to the bottom
        if (LogList != null) {
            LogList.ScrollToItem(CurrentLogs.Count - 1);
        }
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
            UpdateLogList(msg);
    }

    public override void DevLog(string msg, string tag)
    {
        CombatLogMessage message = new CombatLogMessage(msg, new HashSet<string> { "dev", tag.ToLower() });
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(msg);
    }

    public override void DevLog(string msg, List<string> tags)
    {
        HashSet<string> t = new();
        foreach (string tag in tags) 
        {
            t.Add(tag.ToLower());
        }
        t.Add("dev");
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(msg);
    }

    public override void Log(string msg)
    {
        CombatLogMessage message = new CombatLogMessage(msg, new());
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(msg);
    }

    public override void Log(string msg, string tag)
    {
        HashSet<string> t = new HashSet<string> { tag.ToLower() };
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(msg);
    }

    public override void Log(string msg, List<string> tags)
    {
        HashSet<string> t = new();
        foreach (string tag in tags)
        {
            t.Add(tag.ToLower());
        }
        CombatLogMessage message = new CombatLogMessage(msg, t);
        Messages.Add(message);
        if (Filter(message))
            UpdateLogList(msg);
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

    /// <summary>
    /// Helper function, returns true if msg passes filters
    /// </summary>
    /// <param name="msg"></param>
    /// <returns></returns>
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

    private string timestamp()
    {
        return DateTime.Now.ToString("(HH:mm:ss) ");
    }
}
