using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;

[assembly: MelonInfo(typeof(ReplaceMod), "Equipment Replace Button", "1.0.0", "Jahzir")]
[assembly: MelonGame]

public class ReplaceMod : MelonMod
{
    internal static EquipmentSetsUI TrackedInstance;
    static bool _wasActive;
    static int _lastSetCount = -1;

    public override void OnInitializeMelon()
    {
        HarmonyInstance.PatchAll();
    }

    public override void OnLateUpdate()
    {
        var ui = TrackedInstance;
        if (ui == null) return;
        bool active = ui.isActiveAndEnabled && GameVariables.Current != null;

        if (active && !_wasActive)
        {
            PatchEquipmentSetsUI.AddReplaceButtons(ui);
            _lastSetCount = GameVariables.Current.itemManager?.Sets?.Count ?? -1;
        }
        else if (active)
        {
            int count = GameVariables.Current.itemManager?.Sets?.Count ?? -1;
            if (count >= 0 && count != _lastSetCount)
            {
                _lastSetCount = count;
                PatchEquipmentSetsUI.AddReplaceButtons(ui);
            }
        }

        _wasActive = active;
    }
}

[HarmonyPatch(typeof(EquipmentSetsUI), "OnEnable")]
public class PatchEquipmentSetsUIOnEnable
{
    static void Postfix(EquipmentSetsUI __instance)
    {
        ReplaceMod.TrackedInstance = __instance;
        PatchEquipmentSetsUI.AddReplaceButtons(__instance);
    }
}

[HarmonyPatch(typeof(EquipmentSetsUI), "SyncEquipmentSets")]
public class PatchEquipmentSetsUISync
{
    static void Postfix(EquipmentSetsUI __instance)
    {
        MelonCoroutines.Start(DelayedAddButtons(__instance));
    }

    static System.Collections.IEnumerator DelayedAddButtons(EquipmentSetsUI instance)
    {
        yield return null;
        yield return null;
        PatchEquipmentSetsUI.AddReplaceButtons(instance);
    }
}

public class PatchEquipmentSetsUI
{
    const string ButtonName = "ReplaceButton";

    internal static void AddReplaceButtons(EquipmentSetsUI instance)
    {
        if (GameVariables.Current == null) return;

        var scrollRect = instance.GetComponentInChildren<ScrollRect>(true);
        var content = scrollRect?.content;
        if (content == null) return;

        for (int i = 0; i < content.childCount; i++)
        {
            var row = content.GetChild(i);
            Transform stale;
            while ((stale = row.Find(ButtonName)) != null)
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
        }

        for (int i = 0; i < content.childCount; i++)
        {
            var row = content.GetChild(i);
            if (!row.gameObject.activeSelf) continue;

            var buttons = row.GetComponentsInChildren<Button>(true);
            if (buttons.Count == 0) continue;

            Button equipBtn = null, exportBtn = null;
            foreach (var b in buttons)
            {
                string label = b.GetComponentInChildren<Il2CppTMPro.TMP_Text>(true)?.text ?? "";
                if (label.Contains("Equip")) equipBtn = b;
                else if (label.Contains("Export")) exportBtn = b;
            }
            if (equipBtn == null || exportBtn == null) continue;

            string setName = GetRowSetName(row);
            if (setName == null) continue;

            var container = exportBtn.transform.parent;
            var replaceObj = GameObject.Instantiate(exportBtn.gameObject, container);
            replaceObj.name = ButtonName;
            replaceObj.transform.SetAsLastSibling();

            foreach (var comp in replaceObj.GetComponentsInChildren<Component>(true).ToArray())
            {
                var n = comp.GetIl2CppType().FullName ?? "";
                if (n.Contains("Loc") || n.Contains("LOC")) UnityEngine.Object.DestroyImmediate(comp);
            }
            foreach (var comp in replaceObj.GetComponentsInChildren<Component>(true))
            {
                var tmp = comp.TryCast<Il2CppTMPro.TMP_Text>();
                if (tmp != null) { tmp.text = "Replace"; break; }
            }

            if (container.GetComponent<HorizontalLayoutGroup>() == null)
            {
                var exportRectComp = exportBtn.GetComponent<RectTransform>();
                var replaceRect = replaceObj.GetComponent<RectTransform>();
                float newX = exportRectComp.anchoredPosition.x - exportRectComp.rect.width - 5f;
                var ap = replaceRect.anchoredPosition;
                ap.x = newX;
                replaceRect.anchoredPosition = ap;
            }

            var btn = replaceObj.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            Transform capturedRow = row;
            EquipmentSetsUI capturedInstance = instance;
            btn.onClick.AddListener(new Action(() =>
            {
                if (capturedRow == null) return;
                string name = GetRowSetName(capturedRow);
                if (name == null) return;
                OnReplaceClicked(name, capturedInstance);
            }));
        }
    }

    static string CleanText(string raw)
    {
        if (raw == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in raw)
        {
            if (c < ' ' || c == '​' || c == '‌' || c == '‍' || c == '﻿' || c == '­')
                continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    internal static string GetRowSetName(Transform row)
    {
        foreach (var comp in row.GetComponentsInChildren<Component>(true))
        {
            var tmp = comp.TryCast<Il2CppTMPro.TMP_Text>();
            if (tmp == null) continue;

            bool insideButton = false;
            var p = comp.transform.parent;
            while (p != null && p != row)
            {
                if (p.GetComponent<Button>() != null) { insideButton = true; break; }
                p = p.parent;
            }
            if (insideButton) continue;

            string t = CleanText(tmp.text);
            if (t.Length == 0) continue;
            if (char.IsDigit(t[0])) continue;
            if (t.Contains("$")) continue;
            if (t == "Equipment Set") continue;

            return t;
        }
        return null;
    }

    static string InnerMessage(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    static EquipmentSet FindSetByName(string name)
    {
        var col = GameVariables.Current?.itemManager?.Sets;
        if (col == null) return null;

        try
        {
            var rl = col.TryCast<Il2CppSystem.Collections.Generic.IReadOnlyList<EquipmentSet>>();
            if (rl != null)
            {
                int count = col.Count;
                for (int i = 0; i < count; i++)
                {
                    var s = rl[i];
                    if (s != null && CleanText(s.Name) == name) return s;
                }
                return null;
            }
        }
        catch { }

        try
        {
            var list = col.TryCast<Il2CppSystem.Collections.Generic.List<EquipmentSet>>();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s != null && CleanText(s.Name) == name) return s;
                }
                return null;
            }
        }
        catch { }

        return null;
    }

    internal static void OnReplaceClicked(string setName, EquipmentSetsUI instance)
    {
        if (FindSetByName(setName) == null) return;
        try { ShowConfirmDialog(setName, instance); }
        catch { DoReplace(setName, instance); }
    }

    static void ShowConfirmDialog(string setName, EquipmentSetsUI instance)
    {
        UiOverlay.Instance.SimplePopup(
            "Replace Equipment Set",
            $"Replace \"{setName}\" with current equipment and clothes?",
            (Il2CppSystem.Action)new Action(() => DoReplace(setName, instance)),
            (Il2CppSystem.Action)new Action(() => { })
        );
    }

    static void DoReplace(string setName, EquipmentSetsUI instance)
    {
        var existing = FindSetByName(setName);
        if (existing == null) return;

        var generated = EquipmentSet.GenerateFromCurrent(setName);
        var type = existing.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            try { prop.SetValue(existing, prop.GetValue(generated)); }
            catch { }
        }
    }
}
