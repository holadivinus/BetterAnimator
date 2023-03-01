using UnityEngine;
using UnityEditor;
using UnityEditor.Graphs;
using UnityEditor.Animations;

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using HarmonyLib;
using System.Reflection;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif
//
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

[HarmonyPatch(typeof(GenericMenu), "DropDown")]//, new Type[] { typeof(Rect), typeof(bool) })]
public class Patch_GenericMenu_OnAddParameter
{
    public static bool TypeColoring;
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
#if VRC_SDK_VRCSDK3
                __instance.AddItem(new GUIContent("Add From VRC"), false, () => { AddFromVRC(curParameterControllerView, curController); });
#endif
                __instance.AddItem(new GUIContent("Sort (A-Z)"), false, () => { SortParams(curController, (p1, p2) => { return string.Compare(p1.name, p2.name); }); });
                __instance.AddItem(new GUIContent("Sort (Z-A)"), false, () => { SortParams(curController, (p1, p2) => { return -1 * string.Compare(p1.name, p2.name); }); });
                __instance.AddItem(new GUIContent("Sort (Type)"), false, () => 
                { 
                    SortParams
                    (
                        curController, 
                        (p1, p2) => 
                        {
                            return string.Compare
                            (
                                Enum.GetName(typeof(AnimatorControllerParameterType), p1.type) + p1.name,
                                Enum.GetName(typeof(AnimatorControllerParameterType), p2.type) + p1.name
                            ); 
                        }
                    ); 
                }
                );
            }
            //__instance.AddItem(new GUIContent("Type Color"), TypeColoring, () => { TypeColoring = !TypeColoring; });
            return;
        }
    }

    static BindingFlags allInfo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;

    static MethodInfo AnimatorController_AddParameter_getter;
    static PropertyInfo AnimatorController_parameters_getter;
#if VRC_SDK_VRCSDK3
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
#endif

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

    public static void SortParams(RuntimeAnimatorController curController, Comparison<AnimatorControllerParameter> sortAction)
    {
        if (AnimatorController_parameters_getter == null) AnimatorController_parameters_getter = curController.GetType().GetProperty("parameters", allInfo);
        List<AnimatorControllerParameter> paramList = ((AnimatorControllerParameter[])AnimatorController_parameters_getter.GetValue((object)curController)).ToList();

        paramList.Sort(sortAction);

        AnimatorController_parameters_getter.SetValue((object)curController, paramList.ToArray());
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
            __instance.AddItem(new GUIContent("Pack into State Machine"), false, () => { PackIntoStateMachine(curNode); });
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
            if ((node.outputEdges.Count() + node.inputEdges.Count() == 0) && node.GetType().Name == "StateNode")
                targetGraphGUI.selection.Add(node);
        }
        if (GraphGUI_UpdateUnitySelection_getter == null) GraphGUI_UpdateUnitySelection_getter = (targetGraphGUI as object).GetType().GetMethod("UpdateUnitySelection", allInfo);
        GraphGUI_UpdateUnitySelection_getter.Invoke((object)targetGraphGUI, null);
    }
    static MethodInfo ASMGraphGUI_AddStateMachineCallback_getter;
    public static GraphGUI PackGraphUI;
    static void PackIntoStateMachine(Node targetNode)
    {
        Type baseType = targetNode.GetType();

        if (ASMNode_graphGUI_getter == null) ASMNode_graphGUI_getter = baseType.GetField("graphGUI", allInfo);
        GraphGUI graphGUI = (GraphGUI)ASMNode_graphGUI_getter.GetValue(targetNode);

        if (ASMGraphGUI_AddStateMachineCallback_getter == null) ASMGraphGUI_AddStateMachineCallback_getter = graphGUI.GetType().GetMethod("AddStateMachineCallback", allInfo);

        PackGraphUI = graphGUI;
        ASMGraphGUI_AddStateMachineCallback_getter.Invoke((object)graphGUI, new object[] { targetNode.position.center });
        PackGraphUI = null;
    }
}

[HarmonyPatch(typeof(AnimatorStateMachine), "AddStateMachine", new Type[] { typeof(string), typeof(Vector3) })]
class Patch_AnimatorStateMachine_AddStateMachine
{
    static BindingFlags allInfo = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
    static MethodInfo AnimatorStateMachine_MoveState_getter;
    static MethodInfo AnimatorStateMachine_MoveStateMachine_getter;
    static FieldInfo StateNode_state_getter;
    static FieldInfo StateMachineNode_stateMachine_getter;
    static MethodInfo AnimatorState_SetStatePosition_getter;
    static MethodInfo AnimatorState_SetStateMachinePosition_getter;
    public static void Postfix(AnimatorStateMachine __result, AnimatorStateMachine __instance)
    {
        GraphGUI graphGUI = Patch_GenericMenu_ShowAsContext.PackGraphUI;
        if (graphGUI == null) return;

        if (AnimatorStateMachine_MoveState_getter == null) AnimatorStateMachine_MoveState_getter = __instance.GetType().GetMethod("MoveState", allInfo);
        if (AnimatorStateMachine_MoveStateMachine_getter == null) AnimatorStateMachine_MoveStateMachine_getter = __instance.GetType().GetMethod("MoveStateMachine", allInfo);
        if (AnimatorState_SetStatePosition_getter == null) AnimatorState_SetStatePosition_getter = typeof(AnimatorStateMachine).GetMethod("SetStatePosition", allInfo);
        if (AnimatorState_SetStateMachinePosition_getter == null) AnimatorState_SetStateMachinePosition_getter = typeof(AnimatorStateMachine).GetMethod("SetStateMachinePosition", allInfo);

        Vector2 smallestPos = new Vector2(1000, 1000);
        foreach (Node state in graphGUI.selection)
        {
            smallestPos = state.position.position.magnitude < smallestPos.magnitude ? state.position.position : smallestPos;
        }

        foreach (Node selected in graphGUI.selection)
        {
            if (selected.GetType().Name == "StateNode")
            {
                if (StateNode_state_getter == null) StateNode_state_getter = selected.GetType().GetField("state", allInfo);
                AnimatorState selState = (AnimatorState)StateNode_state_getter.GetValue(selected);

                Vector2 prePos = selected.position.center - smallestPos - new Vector2(-300, -200);
                AnimatorStateMachine_MoveState_getter.Invoke(__instance, new object[] { selState, __result });

                for (int i = 0; i < __result.states.Length; i++)
                {
                    ChildAnimatorState newState = __result.states[i];
                    if (newState.state.name == selState.name)
                    {
                        AnimatorState_SetStatePosition_getter.Invoke(__result, new object[] { newState.state, new Vector3(prePos.x, prePos.y) });
                    }
                }
            }
            else if (selected.GetType().Name == "StateMachineNode")
            {
                if (StateMachineNode_stateMachine_getter == null) StateMachineNode_stateMachine_getter = selected.GetType().GetField("stateMachine", allInfo);
                AnimatorStateMachine selStateMachine = (AnimatorStateMachine)StateMachineNode_stateMachine_getter.GetValue(selected);

                Vector2 prePos = selected.position.center - smallestPos - new Vector2(-300, -200);
                AnimatorStateMachine_MoveStateMachine_getter.Invoke(__instance, new object[] { selStateMachine, __result });

                for (int i = 0; i < __result.stateMachines.Length; i++)
                {
                    ChildAnimatorStateMachine newStateMachine = __result.stateMachines[i];
                    if (newStateMachine.stateMachine.name == selStateMachine.name)
                    {
                        AnimatorState_SetStateMachinePosition_getter.Invoke(__result, new object[] { newStateMachine.stateMachine, new Vector3(prePos.x, prePos.y) });
                    }
                }
            }
        }
    }
}

[HarmonyPatch(typeof(AnimatorStateMachine), "AddState", new Type[] { typeof(AnimatorState), typeof(Vector3) })]
class Patch_AnimatorState_MoveState
{
    public static void Prefix()
    {
        Debug.Log(new System.Diagnostics.StackTrace());
    }

}

/*[HarmonyPatch("Unity.UI.Builder.FoldoutWithCheckbox", "RegisterCheckboxValueChangedCallback")]
public class Patch_tester
{
    static void Prefix()
    {
        Debug.Log(new System.Diagnostics.StackTrace());
    }
}*/
