using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;
#if UNITY_IOS
using System.Collections;
using Unity.Collections;
using UnityEngine.XR.ARKit;
#endif

namespace UnityEngine.XR.ARFoundation.Samples
{
    /// <summary>
    /// Demonstrates saving and loading an
    /// <a href="https://developer.apple.com/documentation/arkit/arworldmap">ARWorldMap</a>.
    /// </summary>
    public class ARWorldMapController : MonoBehaviour
    {
        List<string> m_LogMessages;

        [Tooltip("The ARSession component controlling the session from which to generate ARWorldMaps.")]
        [SerializeField]
        ARSession m_ARSession;

        [Tooltip("UI Text component to display error messages")]
        [SerializeField]
        TMPro.TMP_Text m_ErrorText;

        [Tooltip("The UI Text element used to display log messages.")]
        [SerializeField]
        TMPro.TMP_Text m_LogText;

        [Tooltip("The UI Text element used to display the current AR world mapping status.")]
        [SerializeField]
        TMPro.TMP_Text m_MappingStatusText;

        [SerializeField] GameObject anchorButtons;
        [SerializeField] GameObject UIButtons;
        [SerializeField] GameObject minimap;
        [SerializeField] Button m_LoadButton;
        [SerializeField] Button m_SaveButton;
        [SerializeField] Button m_AddButton;

        static string fileName = "my_session.worldmap";
        static string path => Path.Combine(Application.persistentDataPath, fileName);

        public static ARWorldMapController Instance { get; private set; }

        bool supported
        {
            get
            {
#if UNITY_IOS
                return m_ARSession.subsystem is ARKitSessionSubsystem && ARKitSessionSubsystem.worldMapSupported;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Create an <c>ARWorldMap</c> and save it to disk.
        /// </summary>
        public void OnSaveButton()
        {
#if UNITY_IOS
            StartCoroutine(Save());
#endif
        }

        /// <summary>
        /// Load an <c>ARWorldMap</c> from disk and apply it to the current session.
        /// </summary>
        public void OnLoadButton()
        {
#if UNITY_IOS
            if (!File.Exists(path))
            {
                Debug.Log("No ARWorldMap found. Downloading from Firestore...");
                FirebaseManager.DownloadARWorldMap(fileName, (worldMapBytes) =>
                {
                    ParseByteArray(worldMapBytes);
                });
            }
            else
            {
                Debug.Log("Loading ARWorldMap from disk...");
                StartCoroutine(Load());
            }
#endif
        }

        /// <summary>
        /// Reset the <c>ARSession</c>, destroying any existing trackables, such as planes.
        /// Upon loading a saved <c>ARWorldMap</c>, saved trackables will be restored.
        /// </summary>
        public void OnResetButton()
        {
            m_ARSession.Reset();
        }

#if UNITY_IOS
        IEnumerator Save()
        {
            var sessionSubsystem = (ARKitSessionSubsystem)m_ARSession.subsystem;
            if (sessionSubsystem == null)
            {
                Log("No session subsystem available. Could not save.");
                yield break;
            }

            var request = sessionSubsystem.GetARWorldMapAsync();

            while (!request.status.IsDone())
                yield return null;

            if (request.status.IsError())
            {
                Log($"Session serialization failed with status {request.status}");
                yield break;
            }

            var worldMap = request.GetWorldMap();
            request.Dispose();

            ARPlaceAnchor.Instance.SaveAnchors();

            SaveAndDisposeWorldMap(worldMap);
        }

        void SaveAndDisposeWorldMap(ARWorldMap worldMap)
        {
            Log("Serializing ARWorldMap to byte array...");
            var data = worldMap.Serialize(Allocator.Temp);
            Log($"ARWorldMap has {data.Length} bytes.");
            byte[] worldMapBytes = data.ToArray();
            Log("ARWorldMap serialized to byte array.");

            var file = File.Open(path, FileMode.Create);
            var writer = new BinaryWriter(file);
            writer.Write(data.ToArray());
            writer.Close();
            data.Dispose();
            worldMap.Dispose();

            Log($"ARWorldMap written to {path}");

            FirebaseManager.UploadARWorldMap(worldMapBytes, fileName);
        }

        IEnumerator Load()
        {
            var sessionSubsystem = (ARKitSessionSubsystem)m_ARSession.subsystem;
            if (sessionSubsystem == null)
            {
                Log("No session subsystem available. Could not load.");
                yield break;
            }

            FileStream file;
            try
            {
                file = File.Open(path, FileMode.Open);
            }
            catch (FileNotFoundException)
            {
                Debug.LogError("No ARWorldMap was found. Make sure to save the ARWorldMap before attempting to load it.");
                yield break;
            }

            Log($"Reading {path}...");

            const int bytesPerFrame = 1024 * 10;
            var bytesRemaining = file.Length;
            var binaryReader = new BinaryReader(file);
            var allBytes = new List<byte>();
            while (bytesRemaining > 0)
            {
                var bytes = binaryReader.ReadBytes(bytesPerFrame);
                allBytes.AddRange(bytes);
                bytesRemaining -= bytesPerFrame;
                yield return null;
            }

            var data = new NativeArray<byte>(allBytes.Count, Allocator.Temp);
            data.CopyFrom(allBytes.ToArray());

            Log("Deserializing to ARWorldMap...");
            if (ARWorldMap.TryDeserialize(data, out ARWorldMap worldMap))
                data.Dispose();

            if (worldMap.valid)
            {
                Log("Deserialized successfully.");
            }
            else
            {
                Debug.LogError("Data is not a valid ARWorldMap.");
                yield break;
            }

            Log("Apply ARWorldMap to current session.");
            sessionSubsystem.ApplyWorldMap(worldMap);
            // Check for recognized anchors periodically
            StartCoroutine(ARPlaceAnchor.Instance.CheckForAnchorsAndLoad());
        }
#endif

        void Awake()
        {
            m_LogMessages = new List<string>();

            if (Instance != null)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            // GameObject.DontDestroyOnLoad(this.gameObject);
        }

        public void Log(string logMessage)
        {
            m_LogMessages.Add(logMessage);
        }

        static void SetActive(Button button, bool active)
        {
            if (button != null)
                button.gameObject.SetActive(active);
        }

        static void SetActive(TMPro.TMP_Text text, bool active)
        {
            if (text != null)
                text.gameObject.SetActive(active);
        }

        static void SetText(TMPro.TMP_Text text, string value)
        {
            if (text != null)
                text.text = value;
        }

        void Update()
        {
            if (supported)
            {
                if (ARPlaceAnchor.Instance.canEditAnchors)
                {
                    SetActive(m_ErrorText, false);
                    SetActive(m_MappingStatusText, true);
                }
                m_SaveButton.interactable = true;
                m_LoadButton.interactable = true;
                m_AddButton.interactable = true;
            }
            else
            {
                if (ARPlaceAnchor.Instance.canEditAnchors)
                {
                    SetActive(m_ErrorText, true);
                    SetActive(m_MappingStatusText, false);
                }
                m_SaveButton.interactable = false;
                m_LoadButton.interactable = false;
                m_AddButton.interactable = false;
                m_ErrorText.text = "ARWorldMap is not supported on this device.";
            }

#if UNITY_IOS
            var sessionSubsystem = (ARKitSessionSubsystem)m_ARSession.subsystem;
            if (sessionSubsystem == null)
                return;

            var numLogsToShow = 20;
            string msg = "";
            for (int i = Mathf.Max(0, m_LogMessages.Count - numLogsToShow); i < m_LogMessages.Count; ++i)
            {
                msg += m_LogMessages[i];
                msg += "\n";
            }
            SetText(m_LogText, msg);
            SetText(m_MappingStatusText, $"Mapping Status: {sessionSubsystem.worldMappingStatus}");
#endif
        }
        public void TogglePlaceAnchorsUI(bool canPlaceAnchors)
        {
            m_LogText.gameObject.SetActive(canPlaceAnchors);
            m_ErrorText.gameObject.SetActive(canPlaceAnchors);
            m_MappingStatusText.gameObject.SetActive(canPlaceAnchors);
            anchorButtons.SetActive(canPlaceAnchors);
            UIButtons.SetActive(!canPlaceAnchors);
            minimap.SetActive(!canPlaceAnchors);
        }
        private void ParseByteArray(byte[] worldMapBytes)
        {
            var data = new NativeArray<byte>(worldMapBytes.Length, Allocator.Temp);
            data.CopyFrom(worldMapBytes);

            Log("Deserializing to ARWorldMap...");
            if (ARWorldMap.TryDeserialize(data, out ARWorldMap worldMap))
                data.Dispose();

            if (worldMap.valid)
            {
                Log("Deserialized successfully.");
            }
            else
            {
                Debug.LogError("Data is not a valid ARWorldMap.");
                return;
            }

            Log("Apply ARWorldMap to current session.");
            var sessionSubsystem = (ARKitSessionSubsystem)m_ARSession.subsystem;
            sessionSubsystem.ApplyWorldMap(worldMap);
            // Check for recognized anchors periodically
            StartCoroutine(ARPlaceAnchor.Instance.CheckForAnchorsAndLoad());
        }
    }
}
