using UnityEngine;
using UnityEditor;
using UnityEditor.Graphs;

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using HarmonyLib;
using System.Reflection;

using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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

[HarmonyPatch("UnityEditor.Graphs.AnimationStateMachine.GraphGUI", "HandleContextMenu")]
public class Patch_ASMGraphUI_HandleContextMenu
{
    public static GraphGUI CurrentASMGraphUI;
    static void Prefix(GraphGUI __instance)
    {
        CurrentASMGraphUI = __instance;
    }
    static void Postfix()
    {
        CurrentASMGraphUI = null;
    }
}

[HarmonyPatch("UnityEditor.Graphs.ParameterControllerView", "OnAddParameter")]
public class Patch_ParameterControllerView_OnAddParameter
{
    public static object CurrentParameterControllerView;
    static void Prefix(object __instance)
    {
        CurrentParameterControllerView = __instance;
    }
    static void Postfix()
    {
        CurrentParameterControllerView = null;
    }
}

[HarmonyPatch(typeof(GenericMenu), "DropDown")]
public class Patch_GenericMenu_OnAddParameter
{
    public static void Prefix(GenericMenu __instance)
    {
        //Debug.Log(new System.Diagnostics.StackTrace());
        object curParameterControllerView = Patch_ParameterControllerView_OnAddParameter.CurrentParameterControllerView;

        if (curParameterControllerView != null)
        {
            __instance.AddDisabledItem(new GUIContent(""), false);
            __instance.AddDisabledItem(new GUIContent("BetterAnimator funcs:"), false);

            RuntimeAnimatorController curController = GetParameterControllerViewRuntimeAnimator(curParameterControllerView);
            if (curController != null)
            {
                __instance.AddItem(new GUIContent("Add From VRC"), false, () => { AddFromVRC(curParameterControllerView, curController); });


                
            }
            return;
        }
    }

    static MethodInfo AnimatorController_AddParameter_getter;
    static PropertyInfo AnimatorController_parameters_getter;
    public static void AddFromVRC(object curParameterControllerView, RuntimeAnimatorController curController)
    {
        VRCAvatarDescriptor[] avatars = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>();

        if (AnimatorController_AddParameter_getter == null) AnimatorController_AddParameter_getter = curController.GetType().GetMethod("AddParameter", new Type[] { typeof(AnimatorControllerParameter) });

        if (AnimatorController_parameters_getter == null) AnimatorController_parameters_getter = curController.GetType().GetProperty("parameters", allInfo);

        foreach (VRCAvatarDescriptor av in avatars)
        {
            bool avRelated = false;
            foreach (VRCAvatarDescriptor.CustomAnimLayer layer in av.baseAnimationLayers.Concat(av.specialAnimationLayers))
            {
                if (!layer.animatorController) continue;
                avRelated = (layer.animatorController.name == curController.name);
                if (avRelated) break;
            }
            if (!avRelated) continue;

            if (av.expressionParameters != null)
            {
                AnimatorControllerParameter[] curParams = (AnimatorControllerParameter[])AnimatorController_parameters_getter.GetValue((object)curController);

                for (int i = 0; i < av.GetExpressionParameterCount(); i++)
                {
                    VRCExpressionParameters.Parameter curParam = av.GetExpressionParameter(i);
                    AnimatorControllerParameter newParam = new AnimatorControllerParameter();
                    newParam.name = curParam.name;
                    newParam.type = (AnimatorControllerParameterType)Enum.Parse(typeof(AnimatorControllerParameterType), Enum.GetName(typeof(VRCExpressionParameters.ValueType), curParam.valueType));
                    if (!curParams.Where(p => p.name == newParam.name && p.type == newParam.type).Any())
                        AnimatorController_AddParameter_getter.Invoke((object)curController, new object[] { newParam });
                }
            }
        }
    }

    static BindingFlags allInfo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo ParameterControllerView_m_Host_getter;
    static PropertyInfo IAnimatorControllerEditor_animatorController_getter;
    public static RuntimeAnimatorController GetParameterControllerViewRuntimeAnimator(object ParameterControllerView)
    {
        Type baseType = ParameterControllerView.GetType();
        if (ParameterControllerView_m_Host_getter == null) ParameterControllerView_m_Host_getter = baseType.GetField("m_Host", allInfo);
        object m_Host = ParameterControllerView_m_Host_getter.GetValue(ParameterControllerView);

        if (IAnimatorControllerEditor_animatorController_getter == null) IAnimatorControllerEditor_animatorController_getter = m_Host.GetType().GetProperty("animatorController", allInfo);
        RuntimeAnimatorController animer = IAnimatorControllerEditor_animatorController_getter.GetValue(m_Host) as RuntimeAnimatorController;
        return animer;
    }
}

[HarmonyPatch(typeof(GenericMenu), "ShowAsContext")]
public class Patch_GenericMenu_ShowAsContext
{
    static void Prefix(GenericMenu __instance)
    {
        if (Event.current.type != EventType.ContextClick) return;
        //Debug.Log(new System.Diagnostics.StackTrace());

        Node curNode = Patch_StateNode_NodeUI.CurrentStateNode;
        if (curNode != null) // This Context menu is for a State Node in the Animator
        {
            __instance.AddDisabledItem(new GUIContent(""), false);
            __instance.AddDisabledItem(new GUIContent("BetterAnimator funcs:"), false);
            __instance.AddItem(new GUIContent("Select Out Transitions"), false, () => { SelectEdges(curNode, 0); });
            __instance.AddItem(new GUIContent("Select In Transitions"), false, () => { SelectEdges(curNode, 1); });
            __instance.AddItem(new GUIContent("Select Both Transitions"), false, () => { SelectEdges(curNode, 2); });
            return;
        }

        GraphGUI curGraphGUI = Patch_ASMGraphUI_HandleContextMenu.CurrentASMGraphUI;
        if (curGraphGUI != null)
        {
            __instance.AddDisabledItem(new GUIContent(""), false);
            __instance.AddDisabledItem(new GUIContent("BetterAnimator funcs:"), false);
            __instance.AddItem(new GUIContent("Select Unused States"), false, () => { SelectUnusedStates(curGraphGUI); });
            return;
        }

    }
    static BindingFlags allInfo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
    static FieldInfo ASMNode_graphGUI_getter;
    static MethodInfo GraphGUI_UpdateUnitySelection_getter;
    static void SelectEdges(Node targetNode, byte dir)
    {
        Type baseType = targetNode.GetType();

        if (ASMNode_graphGUI_getter == null) ASMNode_graphGUI_getter = baseType.GetField("graphGUI", allInfo);
        GraphGUI graphGUI = (GraphGUI)ASMNode_graphGUI_getter.GetValue(targetNode);

        if (GraphGUI_UpdateUnitySelection_getter == null) GraphGUI_UpdateUnitySelection_getter = graphGUI.GetType().GetMethod("UpdateUnitySelection", allInfo);

        graphGUI.selection.Clear();
        graphGUI.edgeGUI.edgeSelection.Clear();
        IEnumerable<Edge> targetEdges;
        switch (dir)
        {
            case 0:
                targetEdges = targetNode.outputEdges;
                break;
            case 1:
                targetEdges = targetNode.inputEdges;
                break;
            case 2:
                targetEdges = targetNode.outputEdges.Concat(targetNode.inputEdges);
                break;
            default:
                return;
        }

        foreach (Edge edge in targetEdges)
        {
            graphGUI.edgeGUI.edgeSelection.Add(graphGUI.graph.edges.IndexOf(edge));
        }
        GraphGUI_UpdateUnitySelection_getter.Invoke(graphGUI, null);
    }

    static void SelectUnusedStates(GraphGUI targetGraphGUI)
    {
        targetGraphGUI.selection.Clear();
        targetGraphGUI.edgeGUI.edgeSelection.Clear();
        foreach (Node node in targetGraphGUI.graph.nodes)
        {
            if ((node.outputEdges.Count() + node.inputEdges.Count() == 0) && node.name != "Any State" && node.name != "Entry" && node.name != "Exit")
                targetGraphGUI.selection.Add(node);
        }
        if (GraphGUI_UpdateUnitySelection_getter == null) GraphGUI_UpdateUnitySelection_getter = (targetGraphGUI as object).GetType().GetMethod("UpdateUnitySelection", allInfo);
        GraphGUI_UpdateUnitySelection_getter.Invoke((object)targetGraphGUI, null);
    }
}
