using UnityEngine;
using FishNet.Object;
using System.IO;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets.ResourceLocators;

namespace RoachRace.Networking
{
     public class RoachRaceNetMapGen : NetworkBehaviour
     {
          public string catalogFileName = "catalog_0.1.1.bin";
          public string mapGenAddress = "MapGen";

          bool _loadRequested;

          public override void OnStartServer()
          {
               base.OnStartServer();

               // Do not auto-load Addressables catalogs in the Unity Editor.
               // During Play Mode, use the custom inspector button to trigger it manually.
               if (Application.isEditor) return;

               TryLoadCatalog();
          }

          public override void OnStartClient()
          {
               base.OnStartClient();
               if(IsServerInitialized) return;

               // Do not auto-load Addressables catalogs in the Unity Editor.
               // During Play Mode, use the custom inspector button to trigger it manually.
               if (Application.isEditor) return;

               TryLoadCatalog();
          }

          /// <summary>
          /// Manually trigger catalog loading. Intended for use by an Editor inspector button during Play Mode.
          /// Safe to call multiple times; only the first call will do work.
          /// </summary>
          public void LoadCatalogFromInspector()
          {
               if (!Application.isPlaying)
               {
                    Debug.LogWarning($"[{nameof(RoachRaceNetMapGen)}] LoadCatalogFromInspector was called while not playing. Enter Play Mode first.", gameObject);
                    return;
               }

               TryLoadCatalog();
          }

          void TryLoadCatalog()
          {
               if (_loadRequested)
               {
                    Debug.Log($"[{nameof(RoachRaceNetMapGen)}] Catalog load already requested; skipping duplicate request.", gameObject);
                    return;
               }
               _loadRequested = true;

               string platformFolder = Application.platform == RuntimePlatform.OSXEditor ? "StandaloneOSX" :
                                        Application.platform == RuntimePlatform.WindowsEditor ? "StandaloneWindows" :
                                        Application.platform == RuntimePlatform.LinuxEditor ? "StandaloneLinux64" :
                                        Application.platform == RuntimePlatform.OSXPlayer ? "StandaloneOSX" :
                                        Application.platform == RuntimePlatform.WindowsPlayer ? "StandaloneWindows" :
                                        Application.platform == RuntimePlatform.LinuxPlayer ? "StandaloneLinux64" :
                                        "UnsupportedPlatform";
               string root = Application.streamingAssetsPath + "/MapGen/" + platformFolder;
               string catalogPath = Path.Combine(root, catalogFileName);
               Debug.Log($"[{nameof(RoachRaceNetMapGen)}] Loading art catalog from:\n{catalogPath}");
               Addressables.LoadContentCatalogAsync(catalogPath, true).Completed += OnCompletedLoadContentCatalog;
          }

          void OnCompletedLoadContentCatalog(AsyncOperationHandle<IResourceLocator> handle)
          {
               if (handle.Status != AsyncOperationStatus.Succeeded)
               {
                    Debug.LogError($"[{nameof(RoachRaceNetMapGen)}] Failed to load Art Addressables catalog");
               }
               else
               {
                    Debug.Log($"[{nameof(RoachRaceNetMapGen)}] Art Addressables catalog loaded successfully. Loading {mapGenAddress}...");
                    Addressables.InstantiateAsync(mapGenAddress).Completed += OnCompletedLoadScene;
               }
          }

          void OnCompletedLoadScene(AsyncOperationHandle<GameObject> handle)
          {
               if (handle.Status != AsyncOperationStatus.Succeeded)
               {
                    Debug.LogError($"[{nameof(RoachRaceNetMapGen)}] Failed to load map generation scene: {mapGenAddress}");
               }
               else
               {
                    Debug.Log($"[{nameof(RoachRaceNetMapGen)}] Map generation scene loaded successfully: {mapGenAddress}");
               }
          }
    }
}