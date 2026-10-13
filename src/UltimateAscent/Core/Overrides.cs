using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UltimateAscent.Core;

/// <summary>
/// Remembers the original value of every game field this mod overwrites, so the values can be
/// put back when the climb ends, and so multipliers are always applied to the original value
/// instead of compounding.
/// </summary>
internal static class Overrides
{
    private sealed class Change
    {
        internal WeakReference<object> owner;
        internal MemberInfo member;
        internal object original;
        internal object applied;

        internal object Read(object target)
        {
            return member is FieldInfo field ? field.GetValue(target) : ((PropertyInfo)member).GetValue(target, null);
        }

        internal void Write(object target, object value)
        {
            if (member is FieldInfo field)
            {
                field.SetValue(target, value);
            }
            else
            {
                ((PropertyInfo)member).SetValue(target, value, null);
            }
        }
    }

    private static readonly Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();
    private static readonly List<Change> changes = new List<Change>();
    private static ConditionalWeakTable<object, Dictionary<string, Change>> tracked = new ConditionalWeakTable<object, Dictionary<string, Change>>();
    private static ConditionalWeakTable<object, Dictionary<string, float>> baselines = new ConditionalWeakTable<object, Dictionary<string, float>>();

    /// <summary>Sets a field/property and records its original value the first time.</summary>
    internal static void Set(object owner, string name, object value)
    {
        if (owner == null)
        {
            return;
        }
        Dictionary<string, Change> perOwner = tracked.GetOrCreateValue(owner);
        if (!perOwner.TryGetValue(name, out Change change))
        {
            string key = owner.GetType().FullName + ":" + name;
            if (!members.TryGetValue(key, out MemberInfo member))
            {
                for (Type type = owner.GetType(); type != null && member == null; type = type.BaseType)
                {
                    const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    member = (MemberInfo)type.GetField(name, flags) ?? type.GetProperty(name, flags);
                }
                if (member == null)
                {
                    throw new MissingMemberException(key);
                }
                members[key] = member;
            }
            change = new Change { owner = new WeakReference<object>(owner), member = member };
            change.original = change.Read(owner);
            perOwner[name] = change;
            changes.Add(change);
        }
        if (!Equals(change.Read(owner), value))
        {
            change.Write(owner, value);
        }
        change.applied = value;
    }

    /// <summary>Returns the first value seen for <paramref name="key"/> on this object.</summary>
    internal static float Original(object owner, string key, float current)
    {
        Dictionary<string, float> perOwner = baselines.GetOrCreateValue(owner);
        if (!perOwner.TryGetValue(key, out float value))
        {
            value = current;
            perOwner[key] = value;
        }
        return value;
    }

    /// <summary>Restores everything that still holds the value this mod wrote.</summary>
    internal static void RestoreAll()
    {
        foreach (Change change in changes)
        {
            if (!change.owner.TryGetTarget(out object target))
            {
                continue;
            }
            if (target is UnityEngine.Object unityObject && unityObject == null)
            {
                continue;
            }
            try
            {
                if (Equals(change.Read(target), change.applied))
                {
                    change.Write(target, change.original);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not restore a game value: " + ex.Message);
            }
        }
        changes.Clear();
        tracked = new ConditionalWeakTable<object, Dictionary<string, Change>>();
        baselines = new ConditionalWeakTable<object, Dictionary<string, float>>();
    }
}
