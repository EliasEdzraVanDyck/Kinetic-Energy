using UnityEngine;

namespace KineticEnergy.UI
{
    public class TimedMessage : MonoBehaviour
    {
        public float displayDuration = 3f;

        float timer;

        void OnEnable()
        {
            timer = 0f;
        }

        void Update()
        {
            timer += Time.deltaTime;
            if (timer >= displayDuration)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
