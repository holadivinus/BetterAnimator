using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Graphs;
using UnityEngine;
using System.Reflection;
using System;
using HarmonyLib;

[InitializeOnLoad]
public static class BetterAnimator
{
    static BetterAnimator()
    {
        Harmony harmony = new Harmony("com.BetterAnimator.patch");
        harmony.PatchAll();
    }
}

[HarmonyPatch("UnityEditor.Graphs.AnimationStateMachine.StateNode", "NodeUI")]
public class Patch_StateNode_NodeUI
{ 
    public static Node CurrentStateNode;
	static void Prefix(Node __instance)
    {
        CurrentStateNode = __instance;
    }
    static void Postfix()
    {
        CurrentStateNode = null;
    }
}

[HarmonyPatch(typeof(GenericMenu), "ShowAsContext")]
public class Patch_GenericMenu_ShowAsContext
{
    static void Prefix(GenericMenu __instance)
    {
        Node curNode = Patch_StateNode_NodeUI.CurrentStateNode;

        if (Event.current.type == EventType.ContextClick && curNode != null) // This Context menu is for a State Node in the Animator
        {
            __instance.AddDisabledItem(new GUIContent(""), false);
            __instance.AddDisabledItem(new GUIContent("BetterAnimator funcs:"), false);
            __instance.AddItem(new GUIContent("Select Out Transitions"), false, () => { SelectEdges(curNode, true); });
            __instance.AddItem(new GUIContent("Select In Transitions"), false, () => { SelectEdges(curNode, false); });
        }
    }
    static BindingFlags allInfo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo ASMNode_graphGUI_getter;
    static MethodInfo GraphGUI_UpdateUnitySelection_getter;
    static void SelectEdges(Node targetNode, bool dir)
    {
        Type baseType = targetNode.GetType();

        if (ASMNode_graphGUI_getter == null) ASMNode_graphGUI_getter = baseType.GetField("graphGUI", allInfo);
        GraphGUI graphGUI = (GraphGUI)ASMNode_graphGUI_getter.GetValue(targetNode);

        if (GraphGUI_UpdateUnitySelection_getter == null) GraphGUI_UpdateUnitySelection_getter = graphGUI.GetType().GetMethod("UpdateUnitySelection", allInfo);

        graphGUI.selection.Clear();
        graphGUI.edgeGUI.edgeSelection.Clear();
        foreach (Edge edge in dir ? targetNode.outputEdges : targetNode.inputEdges)
        {
            graphGUI.edgeGUI.edgeSelection.Add(graphGUI.graph.edges.IndexOf(edge));
        }
        GraphGUI_UpdateUnitySelection_getter.Invoke(graphGUI, null);
    }
}
