using UnityEditor;
using UnityEngine;
using Mechanics; // Đảm bảo namespace này khớp với FloodController.cs

/// <summary>
/// Custom Property Drawer cho FloodStage để thêm nút bấm hỗ trợ lấy tọa độ nhanh trong Inspector.
/// </summary>
[CustomPropertyDrawer(typeof(FloodController.FloodStage))]
public class FloodStageDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        
        // 1. Vẽ toàn bộ các trường mặc định của Stage (bao gồm cả Foldout)
        // Sử dụng true để vẽ cả các con (children)
        EditorGUI.PropertyField(position, property, label, true);

        // 2. Nếu Stage đang được mở rộng (Expanded) thì vẽ thêm nút bấm ở dưới cùng
        if (property.isExpanded)
        {
            // Tính toán vị trí nút: Lấy tổng chiều cao hiện tại trừ đi phần bù cho nút
            float totalHeight = EditorGUI.GetPropertyHeight(property, label, true);
            Rect buttonRect = new Rect(position.x + 15, position.y + totalHeight - 4, position.width - 15, 20);

            // Đổi màu nút cho dễ nhìn
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f); // Màu xanh lá nhẹ

            if (GUI.Button(buttonRect, new GUIContent("Get Current Position", "Lấy localPosition hiện tại của Flood để điền vào TargetLocalPosition")))
            {
                Object targetObj = property.serializedObject.targetObject;
                if (targetObj is FloodController controller)
                {
                    SerializedProperty posProp = property.FindPropertyRelative("TargetLocalPosition");
                    posProp.vector3Value = controller.transform.localPosition;
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
            GUI.backgroundColor = Color.white; // Reset màu
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Lấy chiều cao mặc định của Unity (đã bao gồm các con khi expanded)
        float height = EditorGUI.GetPropertyHeight(property, label, true);
        
        // Nếu đang mở rộng, cộng thêm 28 pixel để chứa nút bấm
        return property.isExpanded ? height + 28 : height;
    }
}