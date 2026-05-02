using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Text;
using System.Linq;

/// <summary>
/// Công cụ Editor để trích xuất tất cả các trạng thái và chuyển tiếp từ một Animator Controller, 
/// bao gồm cả các Sub-State Machines, và hiển thị chúng dưới dạng ma trận trong Console.
/// </summary>
public class AnimatorStateMachineExtractor : EditorWindow
{
    private Animator _targetAnimator;

    [MenuItem("Tools/Animator State Machine Extractor")]
    public static void ShowWindow()
    {
        GetWindow<AnimatorStateMachineExtractor>("Animator State Machine Extractor");
    }

    private void OnGUI()
    {
        GUILayout.Label("Extract Animator State Machine", EditorStyles.boldLabel);

        _targetAnimator = (Animator)EditorGUILayout.ObjectField("Target Animator", _targetAnimator, typeof(Animator), true);

        if (_targetAnimator == null)
        {
            EditorGUILayout.HelpBox("Please assign an Animator component.", MessageType.Warning);
            return;
        }

        if (GUILayout.Button("Extract State Machine"))
        {
            ExtractStateMachine();
        }
    }

    private void ExtractStateMachine()
    {
        if (_targetAnimator == null)
        {
            Debug.LogError("No Animator assigned.");
            return;
        }

        AnimatorController controller = _targetAnimator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            Debug.LogError("The assigned Animator does not have a valid AnimatorController or it's not an AnimatorController asset.");
            return;
        }

        if (controller.layers.Length == 0)
        {
            Debug.LogWarning("AnimatorController has no layers.");
            return;
        }

        AnimatorControllerLayer baseLayer = controller.layers[0];

        List<AnimatorState> allStates = new List<AnimatorState>();
        List<string> allStatePaths = new List<string>();
        List<TransitionData> anyTransitions = new List<TransitionData>();

        // Flatten cấu trúc đệ quy bao gồm cả các Sub-State Machines
        GatherStatesAndAnyTransitions(baseLayer.stateMachine, "", allStates, allStatePaths, anyTransitions);

        int numStates = allStates.Count;
        // Ma trận: Hàng 0 là "Any State", các hàng tiếp theo là từng State. Cột đại diện cho "To State".
        string[,] transitionMatrix = new string[numStates + 1, numStates];

        // Khởi tạo ma trận
        for (int i = 0; i <= numStates; i++)
            for (int j = 0; j < numStates; j++)
                transitionMatrix[i, j] = "N/A";

        // 1. Điền Any State Transitions (Hàng 0)
        foreach (var data in anyTransitions)
        {
            int destIndex = allStates.IndexOf(data.transition.destinationState);
            if (destIndex != -1)
            {
                string cond = FormatConditions(data.transition.conditions);
                string context = string.IsNullOrEmpty(data.sourcePath) ? "" : $"[{data.sourcePath}] ";
                UpdateMatrixCell(transitionMatrix, 0, destIndex, context + cond);
            }
        }

        // 2. Điền Normal Transitions (Hàng 1 trở đi)
        for (int i = 0; i < numStates; i++)
        {
            foreach (var trans in allStates[i].transitions)
            {
                int destIndex = allStates.IndexOf(trans.destinationState);
                if (destIndex != -1)
                {
                    string cond = FormatConditions(trans.conditions);
                    UpdateMatrixCell(transitionMatrix, i + 1, destIndex, cond);
                }
            }
        }

        // 3. Xây dựng ma trận văn bản phân tách bằng phím Tab (để copy vào Excel dễ dàng)
        StringBuilder sb = new StringBuilder();
        sb.Append("From \\ To State\t");
        foreach (string path in allStatePaths) sb.Append($"{path}\t");
        sb.AppendLine();

        // Hàng Any State
        sb.Append("[Any State]\t");
        for (int j = 0; j < numStates; j++) sb.Append($"{transitionMatrix[0, j]}\t");
        sb.AppendLine();

        // Các hàng State cụ thể
        for (int i = 0; i < numStates; i++)
        {
            sb.Append($"{allStatePaths[i]}\t");
            for (int j = 0; j < numStates; j++)
            {
                sb.Append($"{transitionMatrix[i + 1, j]}\t");
            }
            sb.AppendLine();
        }
        Debug.Log(sb.ToString());
    }

    private void GatherStatesAndAnyTransitions(AnimatorStateMachine sm, string path, List<AnimatorState> states, List<string> paths, List<TransitionData> anyTrans)
    {
        foreach (var childState in sm.states)
        {
            states.Add(childState.state);
            paths.Add(string.IsNullOrEmpty(path) ? childState.state.name : $"{path}/{childState.state.name}");
        }

        foreach (var trans in sm.anyStateTransitions)
        {
            anyTrans.Add(new TransitionData { transition = trans, sourcePath = path });
        }

        foreach (var childSM in sm.stateMachines)
        {
            string subPath = string.IsNullOrEmpty(path) ? childSM.stateMachine.name : $"{path}/{childSM.stateMachine.name}";
            GatherStatesAndAnyTransitions(childSM.stateMachine, subPath, states, paths, anyTrans);
        }
    }

    private void UpdateMatrixCell(string[,] matrix, int row, int col, string condition)
    {
        if (matrix[row, col] == "N/A") matrix[row, col] = condition;
        else matrix[row, col] += " OR " + condition;
    }

    private struct TransitionData
    {
        public AnimatorStateTransition transition;
        public string sourcePath;
    }

    private string FormatConditions(AnimatorCondition[] conditions)
    {
        if (conditions == null || conditions.Length == 0)
        {
            return "Always";
        }

        List<string> condStrings = new List<string>();
        foreach (AnimatorCondition cond in conditions)
        {
            string conditionString = $"{cond.parameter}";
            switch (cond.mode)
            {
                case AnimatorConditionMode.If:
                    conditionString += " is true";
                    break;
                case AnimatorConditionMode.IfNot:
                    conditionString += " is false";
                    break;
                case AnimatorConditionMode.Equals:
                    conditionString += $" == {cond.threshold}";
                    break;
                case AnimatorConditionMode.NotEqual:
                    conditionString += $" != {cond.threshold}";
                    break;
                case AnimatorConditionMode.Greater:
                    conditionString += $" > {cond.threshold}";
                    break;
                case AnimatorConditionMode.Less:
                    conditionString += $" < {cond.threshold}";
                    break;
            }
            condStrings.Add(conditionString);
        }
        return string.Join(" AND ", condStrings);
    }
}