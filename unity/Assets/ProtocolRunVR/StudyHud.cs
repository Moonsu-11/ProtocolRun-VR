using UnityEngine;
using UnityEngine.UI;

namespace ProtocolRunVR
{
    public class StudyHud : MonoBehaviour
    {
        public ProtocolRunClient client;
        public Text instruction, connection;
        public GameObject surveyPanel;
        public Slider difficulty;
        public InputField response;
        private void Start()
        {
            client.onInstruction.AddListener(SetInstruction);
            client.onConnectionStatus.AddListener(SetConnection);
        }
        private void OnDestroy()
        {
            if (!client) return;
            client.onInstruction.RemoveListener(SetInstruction);
            client.onConnectionStatus.RemoveListener(SetConnection);
        }
        private void SetInstruction(string value) { if (instruction) instruction.text = value; }
        private void SetConnection(string value) { if (connection) connection.text = value; }
        private void Update() { if (surveyPanel) surveyPanel.SetActive(client.Current != null && client.Current.step == 5); }
        public void SubmitSurvey() { client.SubmitSurvey(difficulty ? Mathf.Clamp(Mathf.RoundToInt(difficulty.value), 1, 7) : 4, response ? response.text : ""); }
    }
}
