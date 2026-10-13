using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Directiva.MainMenu
{
    /// <summary>Invokes a destination-scene event matching the requested launch ID.</summary>
    public sealed class LevelBootstrapper : MonoBehaviour
    {
        [Serializable]
        public sealed class LaunchEvent
        {
            public string launchId;
            public UnityEvent onLoad = new UnityEvent();
        }

        [SerializeField] private List<LaunchEvent> launchEvents = new List<LaunchEvent>();

        private void Start()
        {
            if (!LevelLaunchContext.TryConsume(out var id)) return;
            foreach (var entry in launchEvents)
            {
                if (entry == null || !string.Equals(entry.launchId, id, StringComparison.Ordinal)) continue;
                entry.onLoad?.Invoke();
                return;
            }
            Debug.LogWarning($"[Directiva] No LevelBootstrapper event is configured for launch ID '{id}' in scene '{gameObject.scene.name}'.", this);
        }
    }
}
