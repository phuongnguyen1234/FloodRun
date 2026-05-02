using UnityEngine;

namespace UI
{
    public class MultiplayerUI : MonoBehaviour
    {
        private void OnEnable()
        {
        }


        public void Close()
        {
            if (HomeUIManager.Instance != null)
            {
                HomeUIManager.Instance.ShowHomeScreen();
            }

            Destroy(gameObject);
        }
    }
}

