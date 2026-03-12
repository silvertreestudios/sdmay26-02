using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The functional aspect of the Combat Log
/// </summary>
public abstract class CombatLogInterface : SingletonMonoBehaviour<CombatLogInterface>
{
    /// <summary>
    /// The Visual element for the combat log
    /// </summary>
    public VisualElement Ui;

    /// <summary>
    /// Displays Dev messages
    /// </summary>
    public abstract void DevMode();
    
    /// <summary>
    /// Hides Dev messages
    /// </summary>
    public abstract void ReleaseMode();

    /// <summary>
    /// Adds a tag that must be present on a message for it to be displayed.
    /// </summary>
    /// <param name="tag"></param>
    public abstract void AddWhiteList(string tag);

    /// <summary>
    /// Adds a tag that must be absent from a message for it to be displayed.
    /// </summary>
    /// <param name="tag"></param>
    public abstract void AddBlackList(string tag);

    /// <summary>
    /// Logs a message with no tags. Only use this if everyone should see it
    /// </summary>
    /// <param name="msg"></param>
    public abstract void Log(string msg);

    /// <summary>
    /// Logs a message with a dev tag
    /// </summary>
    /// <param name="msg"></param>
    public abstract void DevLog(string msg);

    /// <summary>
    /// Logs a message with a dev tag and the given tag
    /// </summary>
    /// <param name="msg"></param>
    public abstract void DevLog(string msg, string tag);

    /// <summary>
    /// Logs a message with a dev tag and the given tags
    /// </summary>
    /// <param name="msg"></param>
    public abstract void DevLog(string msg, List<string> tags);

    /// <summary>
    /// Logs a message with a single tag
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="tag"></param>
    public abstract void Log(string msg, string tag);

    /// <summary>
    /// Logs a message with multiple tags
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="tags"></param>
    public abstract void Log(string msg, List<string> tags);

    public abstract List<string> GetMessages();
}
