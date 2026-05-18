#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;

/// <summary>
/// Custom Property Drawer để hỗ trợ chọn loại MapAction trong Inspector khi dùng [SerializeReference].
/// </summary>
[CustomPropertyDrawer(typeof(MapAction), true)]
public class MapActionPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // 1. Hiển thị Label của element (ví dụ: Element 0, Element 1)
        Rect labelRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        // 2. Nút bấm để chọn loại Action
        Rect buttonRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);

        string typeName = property.managedReferenceFullTypename;
        if (string.IsNullOrEmpty(typeName)) typeName = "Null (Select Action Type)";
        else typeName = typeName.Split('.').Last(); // Chỉ lấy tên Class cuối cùng

        if (GUI.Button(buttonRect, typeName, EditorStyles.popup))
        {
            ShowTypeMenu(property);
        }

        // 3. Vẽ nội dung của Action bên dưới nếu nó không null
        if (!string.IsNullOrEmpty(property.managedReferenceFullTypename))
        {
            EditorGUI.PropertyField(position, property, label, true);
        }
        else
        {
            EditorGUI.LabelField(labelRect, label);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Nếu null thì chỉ cao bằng 1 dòng, nếu có dữ liệu thì lấy chiều cao của toàn bộ thuộc tính
        if (string.IsNullOrEmpty(property.managedReferenceFullTypename))
            return EditorGUIUtility.singleLineHeight;
            
        return EditorGUI.GetPropertyHeight(property, true);
    }

    private void ShowTypeMenu(SerializedProperty property)
    {
        GenericMenu menu = new GenericMenu();

        // Tìm tất cả các class kế thừa từ MapAction (trừ abstract class)
        var types = TypeCache.GetTypesDerivedFrom<MapAction>()
            .Where(t => !t.IsAbstract && !t.IsGenericType);

        menu.AddItem(new GUIContent("None"), string.IsNullOrEmpty(property.managedReferenceFullTypename), () => {
            property.managedReferenceValue = null;
            property.serializedObject.ApplyModifiedProperties();
        });

        foreach (var type in types)
        {
            string menuPath = type.Name.Replace("MapAction_", "").Replace("_", "/");
            bool isSelected = property.managedReferenceFullTypename.Contains(type.Name);

            menu.AddItem(new GUIContent(menuPath), isSelected, () => {
                // Khởi tạo instance của class được chọn và gán vào reference
                object instance = Activator.CreateInstance(type);
                property.managedReferenceValue = instance;
                property.serializedObject.ApplyModifiedProperties();
            });
        }

        menu.ShowAsContext();
    }
}
#endif