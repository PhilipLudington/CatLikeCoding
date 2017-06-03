using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_5_5_OR_NEWER
using UnityEngine.Profiling;
#endif

// /////////////////////////////////////////////////////////////////////////////////////////
//                              More Effective Coroutines Pro
//                                        v1.11.0
// 
// This is an improved implementation of coroutines that boasts zero per-frame memory allocations,
// runs about twice as fast as Unity's built in coroutines and has a range of extra features.
// 
// For manual, support, or upgrade guide visit http://trinary.tech/
//
// Created by Teal Rogers
// Trinary Software
// All rights preserved
// trinaryllc@gmail.com
// /////////////////////////////////////////////////////////////////////////////////////////

namespace MovementEffects
{
    public class Timing : MonoBehaviour
    {
        private class WaitingProcess
        {
            public class ProcessData
            {
                public IEnumerator<float> Task;
                public string Tag;
                public int Layer;
                public Segment Segment;
                public double PauseTime;
            }

            public IEnumerator<float> Trigger;
            public string TriggerTag;
            public int TriggerLayer;
            public bool Killed;
            public readonly List<ProcessData> Tasks = new List<ProcessData>();
        }

        private struct ProcessIndex : System.IEquatable<ProcessIndex>
        {
            public Segment seg;
            public int i;

            public bool Equals(ProcessIndex other)
            {
                return seg == other.seg && i == other.i;
            }

            public override bool Equals(object other)
            {
                if (other is ProcessIndex)
                    return Equals((ProcessIndex)other);
                return false;
            }

            public static bool operator==(ProcessIndex a, ProcessIndex b)
            {
                return a.seg == b.seg && a.i == b.i;
            }

            public static bool operator !=(ProcessIndex a, ProcessIndex b)
            {
                return a.seg != b.seg || a.i != b.i;
            }

            public override int GetHashCode()
            {
                return (((int)seg - 4) * (int.MaxValue / 7)) + i;
            }
        }

        public enum DebugInfoType
        {
            None,
            SeperateCoroutines,
            SeperateTags
        }

        public float TimeBetweenSlowUpdateCalls = 1f / 7f;
        public DebugInfoType ProfilerDebugAmount = DebugInfoType.SeperateCoroutines;
        public int UpdateCoroutines;
        public int FixedUpdateCoroutines;
        public int LateUpdateCoroutines;
        public int SlowUpdateCoroutines;
        public int RealtimeUpdateCoroutines;
        public int EditorUpdateCoroutines;
        public int EditorSlowUpdateCoroutines;
        public int EndOfFrameCoroutines;
        public int ManualTimeframeCoroutines;

        [HideInInspector] public double localTime;
        public static float LocalTime { get { return (float)Instance.localTime; } }
        [HideInInspector] public float deltaTime;
        public static float DeltaTime { get { return Instance.deltaTime; } }
        [HideInInspector] public bool AutoTriggerManualTimeframeDuringUpdate = true;

        private bool _runningUpdate;
        private bool _runningFixedUpdate;
        private bool _runningLateUpdate;
        private bool _runningSlowUpdate;
        private bool _runningRealtimeUpdate;
        private bool _runningEditorUpdate;
        private bool _runningEditorSlowUpdate;
        private bool _runningManualTimeframe;
        private int _nextUpdateProcessSlot;
        private int _nextLateUpdateProcessSlot;
        private int _nextFixedUpdateProcessSlot;
        private int _nextSlowUpdateProcessSlot;
        private int _nextRealtimeUpdateProcessSlot;
        private int _nextEditorUpdateProcessSlot;
        private int _nextEditorSlowUpdateProcessSlot;
        private int _nextEndOfFrameProcessSlot;
        private int _nextManualTimeframeProcessSlot;
        private double _lastUpdateTime;
        private double _lastLateUpdateTime;
        private double _lastFixedUpdateTime;
        private double _lastSlowUpdateTime;
        private double _lastRealtimeUpdateTime;
        private double _lastEditorUpdateTime;
        private double _lastEditorSlowUpdateTime;
        private double _lastManualTimeframeTime;
        private ushort _framesSinceUpdate;
        private ushort _expansions = 1;
        private bool _EOFPumpRan;

        private const ushort FramesUntilMaintenance = 64;
        private const int ProcessArrayChunkSize = 64;
        private const int InitialBufferSizeLarge = 256;
        private const int InitialBufferSizeMedium = 64;
        private const int InitialBufferSizeSmall = 8;

        public System.Action<System.Exception> OnError;
        public System.Func<double, double> SetManualTimeframeTime; 
        public static System.Func<IEnumerator<float>, Segment, int, string, IEnumerator<float>> ReplacementFunction;

        private readonly WaitForEndOfFrame _EOFWaitObject = new WaitForEndOfFrame();
        private readonly List<WaitingProcess> _waitingProcesses = new List<WaitingProcess>();
        private readonly Queue<System.Exception> _exceptions = new Queue<System.Exception>();
        private readonly Dictionary<ProcessIndex, string> _processTags = new Dictionary<ProcessIndex, string>();
        private readonly Dictionary<string, List<ProcessIndex>> _taggedProcesses = new Dictionary<string, List<ProcessIndex>>();
        private readonly Dictionary<ProcessIndex, int> _processLayers = new Dictionary<ProcessIndex, int>();
        private readonly Dictionary<int, List<ProcessIndex>> _layeredProcesses = new Dictionary<int, List<ProcessIndex>>();

        private IEnumerator<float>[] UpdateProcesses = new IEnumerator<float>[InitialBufferSizeLarge];
        private IEnumerator<float>[] LateUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
        private IEnumerator<float>[] FixedUpdateProcesses = new IEnumerator<float>[InitialBufferSizeMedium];
        private IEnumerator<float>[] SlowUpdateProcesses = new IEnumerator<float>[InitialBufferSizeMedium];
        private IEnumerator<float>[] RealtimeUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
        private IEnumerator<float>[] EditorUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
        private IEnumerator<float>[] EditorSlowUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
        private IEnumerator<float>[] EndOfFrameProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
        private IEnumerator<float>[] ManualTimeframeProcesses = new IEnumerator<float>[InitialBufferSizeSmall];

#if UNITY_EDITOR
        private bool _editorPaused;
#endif

        private static Timing _instance;
        public static Timing Instance
        {
            get
            {
                if (_instance == null || !_instance.gameObject)
                {
                    GameObject instanceHome = GameObject.Find("Movement Effects");
                    System.Type movementType =
                        System.Type.GetType("MovementEffects.Movement, MovementOverTime, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");

                    if(instanceHome == null)
                    {
                        instanceHome = new GameObject { name = "Movement Effects" };
                        DontDestroyOnLoad(instanceHome);

                        if (movementType != null)
                            instanceHome.AddComponent(movementType);

                        _instance = instanceHome.AddComponent<Timing>();
                    }
                    else
                    {
                         if (movementType != null && instanceHome.GetComponent(movementType) == null) 
                            instanceHome.AddComponent(movementType);

                        _instance = instanceHome.GetComponent<Timing>() ?? instanceHome.AddComponent<Timing>();
                    }
                }

                return _instance;
            }

            set { _instance = value; }
        }

        void Awake()
        {
            if(_instance == null)
                _instance = this;
            else
                deltaTime = Instance.deltaTime;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void OnEnable()
        {
            if(_nextEditorUpdateProcessSlot > 0 || _nextEditorSlowUpdateProcessSlot > 0)
                OnEditorStart();

            if(_nextEndOfFrameProcessSlot > 0)
                RunCoroutineSingletonOnInstance(_EOFPumpWatcher(), "MEC_EOFPumpWatcher");
        }

        private bool OnEditorStart()
        {
#if UNITY_EDITOR
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            if(_lastEditorUpdateTime == 0d)
                _lastEditorUpdateTime = EditorApplication.timeSinceStartup;

            EditorApplication.update -= OnEditorUpdate;

            if (!_editorPaused)
                EditorApplication.update += OnEditorUpdate;

            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private void OnEditorUpdate()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
            {
                for(int i = 0;i < _nextEditorUpdateProcessSlot;i++)
                    EditorUpdateProcesses[i] = null;
                _nextEditorUpdateProcessSlot = 0;
                for (int i = 0; i < _nextEditorSlowUpdateProcessSlot; i++)
                    EditorSlowUpdateProcesses[i] = null;
                _nextEditorSlowUpdateProcessSlot = 0;

                EditorApplication.update -= OnEditorUpdate;
                _instance = null;
            }

            if (_lastEditorSlowUpdateTime + TimeBetweenSlowUpdateCalls < EditorApplication.timeSinceStartup && _nextEditorSlowUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.EditorSlowUpdate };
                _runningEditorSlowUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextEditorSlowUpdateProcessSlot; coindex.i++)
                {
                    if (EditorSlowUpdateProcesses[coindex.i] != null && !(EditorApplication.timeSinceStartup < EditorSlowUpdateProcesses[coindex.i].Current))
                    {
                        try
                        {
                            if (!EditorSlowUpdateProcesses[coindex.i].MoveNext())
                            {
                                EditorSlowUpdateProcesses[coindex.i] = null;
                            }
                            else if (EditorSlowUpdateProcesses[coindex.i] != null && float.IsNaN(EditorSlowUpdateProcesses[coindex.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    EditorSlowUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    EditorSlowUpdateProcesses[coindex.i] = ReplacementFunction(EditorSlowUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0, 
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            EditorSlowUpdateProcesses[coindex.i] = null;
                        }
                    }
                }

                _runningEditorSlowUpdate = false;
            }

            if(_nextEditorUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.EditorUpdate };
                _runningEditorUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextEditorUpdateProcessSlot; coindex.i++)
                {
                    if (EditorUpdateProcesses[coindex.i] != null && !(EditorApplication.timeSinceStartup < EditorUpdateProcesses[coindex.i].Current))
                    {
                        try
                        {
                            if (!EditorUpdateProcesses[coindex.i].MoveNext())
                            {
                                EditorUpdateProcesses[coindex.i] = null;
                            }
                            else if (EditorUpdateProcesses[coindex.i] != null && float.IsNaN(EditorUpdateProcesses[coindex.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    EditorUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    EditorUpdateProcesses[coindex.i] = ReplacementFunction(EditorUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            EditorUpdateProcesses[coindex.i] = null;
                        }
                    }
                }

                _runningEditorUpdate = false;
            }

            if (++_framesSinceUpdate > FramesUntilMaintenance)
            {
                _framesSinceUpdate = 0;

                EditorRemoveUnused();
            }

            if (_exceptions.Count > 0)
                throw _exceptions.Dequeue();
        }
#endif

        private IEnumerator<float> _EOFPumpWatcher()
        {
            while (_nextEndOfFrameProcessSlot > 0)
            {
                if(!_EOFPumpRan)
                    base.StartCoroutine(_EOFPump());

                _EOFPumpRan = false;

                yield return 0f;
            }

            _EOFPumpRan = false;
        }

        private System.Collections.IEnumerator _EOFPump()
        {
            while(_nextEndOfFrameProcessSlot > 0)
            {
                yield return _EOFWaitObject;

                ProcessIndex coindex = new ProcessIndex { seg = Segment.EndOfFrame };
                _EOFPumpRan = true;
                UpdateTimeValues(coindex.seg);

                for(coindex.i = 0;coindex.i < _nextEndOfFrameProcessSlot;coindex.i++)
                {
                    if(EndOfFrameProcesses[coindex.i] != null && !(localTime < EndOfFrameProcesses[coindex.i].Current))
                    {
                        if(ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine, layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex)   ? ", tag " + _processTags[coindex]   : ", no tag"))
                                                     : "Processing Coroutine");
                        }

                        try
                        {
                            if(!EndOfFrameProcesses[coindex.i].MoveNext())
                            {
                                EndOfFrameProcesses[coindex.i] = null;
                            }
                            else if(EndOfFrameProcesses[coindex.i] != null && float.IsNaN(EndOfFrameProcesses[coindex.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    EndOfFrameProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    EndOfFrameProcesses[coindex.i] = ReplacementFunction(EndOfFrameProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if(OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            EndOfFrameProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }
            }
        }

        public void TriggerManualTimeframeUpdate()
        {
            if (_nextManualTimeframeProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.ManualTimeframe };
                _runningManualTimeframe = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextManualTimeframeProcessSlot; coindex.i++)
                {
                    if (ManualTimeframeProcesses[coindex.i] != null && !(localTime < ManualTimeframeProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine (Manual Timeframe), layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine (Manual Timeframe)");
                        }

                        try
                        {
                            if (!ManualTimeframeProcesses[coindex.i].MoveNext())
                            {
                                ManualTimeframeProcesses[coindex.i] = null;
                            }
                            else if (ManualTimeframeProcesses[coindex.i] != null && float.IsNaN(ManualTimeframeProcesses[coindex.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    ManualTimeframeProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    ManualTimeframeProcesses[coindex.i] = ReplacementFunction(ManualTimeframeProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            ManualTimeframeProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningManualTimeframe = false;
            }

            if (++_framesSinceUpdate > FramesUntilMaintenance)
            {
                _framesSinceUpdate = 0;

                if (ProfilerDebugAmount != DebugInfoType.None) 
                    Profiler.BeginSample("Maintenance Task");

                RemoveUnused();

                if (ProfilerDebugAmount != DebugInfoType.None) 
                    Profiler.EndSample();
            }

            if (_exceptions.Count > 0)
                throw _exceptions.Dequeue();
        }

        private void Update()
        {
            if(_lastSlowUpdateTime + TimeBetweenSlowUpdateCalls < Time.realtimeSinceStartup && _nextSlowUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.SlowUpdate };
                _runningSlowUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextSlowUpdateProcessSlot; coindex.i++)
                {
                    if (SlowUpdateProcesses[coindex.i] != null && !(localTime < SlowUpdateProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine (Slow Update), layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine (Slow Update)");
                        }

                        try
                        {
                            if (!SlowUpdateProcesses[coindex.i].MoveNext())
                            {
                                SlowUpdateProcesses[coindex.i] = null;
                            }
                            else if (SlowUpdateProcesses[coindex.i] != null && float.IsNaN(SlowUpdateProcesses[coindex.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    SlowUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    SlowUpdateProcesses[coindex.i] = ReplacementFunction(SlowUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            SlowUpdateProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningSlowUpdate = false;
            }

            if (_nextRealtimeUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.RealtimeUpdate };
                _runningRealtimeUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextRealtimeUpdateProcessSlot; coindex.i++)
                {
                    if (RealtimeUpdateProcesses[coindex.i] != null && !(localTime < RealtimeUpdateProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine (Realtime Update), layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine (Realtime Update)");
                        }

                        try
                        {
                            if (!RealtimeUpdateProcesses[coindex.i].MoveNext())
                            {
                                RealtimeUpdateProcesses[coindex.i] = null;
                            }
                            else if (RealtimeUpdateProcesses[coindex.i] != null && float.IsNaN(RealtimeUpdateProcesses[coindex.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    RealtimeUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    RealtimeUpdateProcesses[coindex.i] = ReplacementFunction(RealtimeUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            RealtimeUpdateProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningRealtimeUpdate = false;
            }

            if (_nextUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.Update };
                _runningUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextUpdateProcessSlot; coindex.i++)
                {
                    if (UpdateProcesses[coindex.i] != null && !(localTime < UpdateProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine, layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine");
                        }

                        try
                        {
                            if (!UpdateProcesses[coindex.i].MoveNext())
                            {
                                UpdateProcesses[coindex.i] = null;
                            }
                            else if (UpdateProcesses[coindex.i] != null && float.IsNaN(UpdateProcesses[coindex.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    UpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    UpdateProcesses[coindex.i] = ReplacementFunction(UpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            UpdateProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningUpdate = false;
            }

            if(AutoTriggerManualTimeframeDuringUpdate)
            {
                TriggerManualTimeframeUpdate();
            }
            else
            {
                if(++_framesSinceUpdate > FramesUntilMaintenance)
                {
                    _framesSinceUpdate = 0;

                    if (ProfilerDebugAmount != DebugInfoType.None) 
                        Profiler.BeginSample("Maintenance Task");

                    RemoveUnused();

                    if (ProfilerDebugAmount != DebugInfoType.None) 
                        Profiler.EndSample();
                }

                if(_exceptions.Count > 0)
                    throw _exceptions.Dequeue();
            }
        }

        private void FixedUpdate()
        {
            if(_nextFixedUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.FixedUpdate };
                _runningFixedUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextFixedUpdateProcessSlot; coindex.i++)
                {
                    if (FixedUpdateProcesses[coindex.i] != null && !(localTime < FixedUpdateProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine, layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine");
                        }

                        try
                        {
                            if (!FixedUpdateProcesses[coindex.i].MoveNext())
                            {
                                FixedUpdateProcesses[coindex.i] = null;
                            }
                            else if (FixedUpdateProcesses[coindex.i] != null && float.IsNaN(FixedUpdateProcesses[coindex.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    FixedUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    FixedUpdateProcesses[coindex.i] = ReplacementFunction(FixedUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            FixedUpdateProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningFixedUpdate = false;
            }

            if (_exceptions.Count > 0)
                throw _exceptions.Dequeue();
        }

        private void LateUpdate()
        {
            if(_nextLateUpdateProcessSlot > 0)
            {
                ProcessIndex coindex = new ProcessIndex { seg = Segment.LateUpdate };
                _runningLateUpdate = true;
                UpdateTimeValues(coindex.seg);

                for (coindex.i = 0; coindex.i < _nextLateUpdateProcessSlot; coindex.i++)
                {
                    if (LateUpdateProcesses[coindex.i] != null && !(localTime < LateUpdateProcesses[coindex.i].Current))
                    {
                        if (ProfilerDebugAmount != DebugInfoType.None)
                        {
                            Profiler.BeginSample(ProfilerDebugAmount == DebugInfoType.SeperateTags
                                                     ? ("Processing Coroutine, layer " +
                                                        (_processLayers.ContainsKey(coindex) ? _processLayers[coindex].ToString() : "0") +
                                                        (_processTags.ContainsKey(coindex) ? ", tag " + _processTags[coindex] : ", no tag"))
                                                     : "Processing Coroutine");
                        }

                        try
                        {
                            if (!LateUpdateProcesses[coindex.i].MoveNext())
                            {
                                LateUpdateProcesses[coindex.i] = null;
                            }
                            else if (LateUpdateProcesses[coindex.i] != null && float.IsNaN(LateUpdateProcesses[coindex.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    LateUpdateProcesses[coindex.i] = null;
                                }
                                else
                                {
                                    LateUpdateProcesses[coindex.i] = ReplacementFunction(LateUpdateProcesses[coindex.i],
                                        coindex.seg, _processLayers.ContainsKey(coindex) ? _processLayers[coindex] : 0,
                                        _processTags.ContainsKey(coindex) ? _processTags[coindex] : null);

                                    ReplacementFunction = null;
                                    coindex.i--;
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            LateUpdateProcesses[coindex.i] = null;
                        }

                        if (ProfilerDebugAmount != DebugInfoType.None) 
                            Profiler.EndSample();
                    }
                }

                _runningLateUpdate = false;
            }

            if (_exceptions.Count > 0)
                throw _exceptions.Dequeue();
        }

        private void UpdateTimeValues(Segment segment)
        {
            switch(segment)
            {
                case Segment.Update:
                    deltaTime = Time.deltaTime;
                    _lastUpdateTime += deltaTime;
                    localTime = _lastUpdateTime;
                    return;
                case Segment.LateUpdate:
                    deltaTime = Time.deltaTime;
                    _lastLateUpdateTime += deltaTime;
                    localTime = _lastLateUpdateTime;
                    return;
                case Segment.FixedUpdate:
                    deltaTime = Time.deltaTime;
                    _lastFixedUpdateTime += deltaTime;
                    localTime = _lastFixedUpdateTime;
                    return;
                case Segment.SlowUpdate:
                    if(_lastSlowUpdateTime == 0d)
                        deltaTime = TimeBetweenSlowUpdateCalls;
                    else
                        deltaTime = Time.realtimeSinceStartup - (float)_lastSlowUpdateTime;

                    localTime = _lastSlowUpdateTime = Time.realtimeSinceStartup;
                    return;
                case Segment.RealtimeUpdate:
                    deltaTime = Time.unscaledDeltaTime;
                    _lastRealtimeUpdateTime += deltaTime;
                    localTime = _lastRealtimeUpdateTime;
                    return;
#if UNITY_EDITOR
                case Segment.EditorUpdate:
                    deltaTime = (float)(EditorApplication.timeSinceStartup - _lastEditorUpdateTime);

                    if(deltaTime > Time.maximumDeltaTime)
                        deltaTime = Time.maximumDeltaTime;

                    localTime = _lastEditorUpdateTime = EditorApplication.timeSinceStartup;
                    return;
                case Segment.EditorSlowUpdate:
                    deltaTime = (float)(EditorApplication.timeSinceStartup - _lastEditorSlowUpdateTime);
                    localTime = _lastEditorSlowUpdateTime = EditorApplication.timeSinceStartup;
                    return;
#endif
                case Segment.EndOfFrame:
                    deltaTime = Time.deltaTime;
                    localTime = _lastUpdateTime;
                    return;
                case Segment.ManualTimeframe:
                    localTime = SetManualTimeframeTime == null ? Time.time : SetManualTimeframeTime(_lastManualTimeframeTime);
                    deltaTime = (float)(localTime - _lastManualTimeframeTime);

                    if (deltaTime > Time.maximumDeltaTime)
                        deltaTime = Time.maximumDeltaTime;

                    _lastManualTimeframeTime = localTime;
                    return;
            }
        }

        private void SetTimeValues(Segment segment)
        {
            switch (segment)
            {
                case Segment.Update:
                    deltaTime = Time.deltaTime;
                    localTime = _lastUpdateTime;
                    return;
                case Segment.LateUpdate:
                    deltaTime = Time.deltaTime;
                    localTime = _lastLateUpdateTime;
                    return;
                case Segment.FixedUpdate:
                    deltaTime = Time.deltaTime;
                    localTime = _lastFixedUpdateTime;
                    return;
                case Segment.SlowUpdate:
                    deltaTime = Time.realtimeSinceStartup - (float)_lastSlowUpdateTime;
                    localTime = _lastSlowUpdateTime = Time.realtimeSinceStartup;
                    return;
                case Segment.RealtimeUpdate:
                    deltaTime = Time.unscaledDeltaTime;
                    localTime = _lastRealtimeUpdateTime;
                    return;
#if UNITY_EDITOR
                case Segment.EditorUpdate:
                    deltaTime = (float)(EditorApplication.timeSinceStartup - _lastEditorUpdateTime);

                    if (deltaTime > Time.maximumDeltaTime)
                        deltaTime = Time.maximumDeltaTime;

                    localTime = _lastEditorUpdateTime = EditorApplication.timeSinceStartup;
                    return;
                case Segment.EditorSlowUpdate:
                    deltaTime = (float)(EditorApplication.timeSinceStartup - _lastEditorSlowUpdateTime);
                    localTime = _lastEditorSlowUpdateTime = EditorApplication.timeSinceStartup;
                    return;
#endif
                case Segment.EndOfFrame:
                    deltaTime = Time.deltaTime;
                    localTime = _lastUpdateTime;
                    return;
                case Segment.ManualTimeframe:
                    deltaTime = Time.deltaTime;
                    localTime = _lastManualTimeframeTime;
                    return;
            }
        }

        /// <summary>
        /// Resets the value of LocalTime to zero (only for the Update, LateUpdate, FixedUpdate, and RealtimeUpdate loops).
        /// </summary>
        public void ResetTimeCountOnInstance()
        {
            localTime = 0d;

            _lastUpdateTime = 0d;
            _lastLateUpdateTime = 0d;
            _lastFixedUpdateTime = 0d;
            _lastRealtimeUpdateTime = 0d;

            _EOFPumpRan = false;
        }

        /// <summary>
        /// This will pause all coroutines running on the current MEC instance until ResumeCoroutines is called.
        /// </summary>
        public static void PauseCoroutines()
        {
            if (_instance != null)
                _instance.PauseCoroutinesOnInstance();
        }

        /// <summary>
        /// This will pause all coroutines running on this MEC instance until ResumeCoroutinesOnInstance is called.
        /// </summary>
        public void PauseCoroutinesOnInstance()
        {
#if UNITY_EDITOR
            if(EditorApplication.isPlaying)
            {
                enabled = false;
            }
            else
            {
                _editorPaused = true;
                EditorApplication.update -= OnEditorUpdate;
            }
#else
            enabled = false;
#endif
        }

        /// <summary>
        /// This will pause any matching coroutines running on the current MEC instance until ResumeCoroutines is called.
        /// </summary>
        /// <param name="tag">Any coroutines with a matching tag will be paused.</param>
        public static void PauseCoroutines(string tag)
        {
            if (_instance != null)
                _instance.PauseCoroutinesOnInstance(tag);
        }

        /// <summary>
        /// This will pause any matching coroutines running on this MEC instance until ResumeCoroutinesOnInstance is called.
        /// </summary>
        /// <param name="tag">Any coroutines with a matching tag will be paused.</param>
        public void PauseCoroutinesOnInstance(string tag)
        {
            if(tag == null) return;

            List<ProcessIndex> matches;
            if(!_taggedProcesses.TryGetValue(tag, out matches))
                return;

            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            WaitingProcess pausedProcs = new WaitingProcess();

            for(int i = 0;i < matches.Count;i++)
            {
                WaitingProcess.ProcessData procData = new WaitingProcess.ProcessData
                {
                    Segment = matches[i].seg,
                    Layer = RemoveLayer(matches[i]),
                    Tag = RemoveTag(matches[i]),
                    Task = CoindexExtract(matches[i])
                };

                SetTimeValues(procData.Segment);
                if (procData.Task.Current > localTime)
                    procData.PauseTime = procData.Task.Current - localTime;

                pausedProcs.Tasks.Add(procData);
            }

            _waitingProcesses.Add(pausedProcs);

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// This will pause any matching coroutines running on the current MEC instance until ResumeCoroutines is called.
        /// </summary>
        /// <param name="layer">Any coroutines on the matching layer will be paused.</param>
        public static void PauseCoroutines(int layer)
        {
            if (_instance != null)
                _instance.PauseCoroutinesOnInstance(layer);
        }

        /// <summary>
        /// This will pause any matching coroutines running on this MEC instance until ResumeCoroutinesOnInstance is called.
        /// </summary>
        /// <param name="layer">Any coroutines on the matching layer will be paused.</param>
        public void PauseCoroutinesOnInstance(int layer)
        {
            if (layer == 0) return;

            List<ProcessIndex> matches;
            if (!_layeredProcesses.TryGetValue(layer, out matches))
                return;

            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            WaitingProcess pausedProcs = new WaitingProcess();

            for(int i = 0;i < matches.Count;i++)
            {
                WaitingProcess.ProcessData procData = new WaitingProcess.ProcessData
                {
                    Segment = matches[i].seg,
                    Layer = RemoveLayer(matches[i]),
                    Tag = RemoveTag(matches[i]),
                    Task = CoindexExtract(matches[i])
                };

                SetTimeValues(procData.Segment);
                if (procData.Task.Current > localTime)
                    procData.PauseTime = procData.Task.Current - localTime;

                pausedProcs.Tasks.Add(procData);
            }

            _waitingProcesses.Add(pausedProcs);

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// This will pause any matching coroutines running on the current MEC instance until ResumeCoroutines is called.
        /// </summary>
        /// <param name="layer">Any coroutines on the matching layer will be paused.</param>
        /// <param name="tag">Any coroutines with a matching tag will be paused.</param>
        public static void PauseCoroutines(int layer, string tag)
        {
            if (_instance != null)
                _instance.PauseCoroutinesOnInstance(layer, tag);
        }

        /// <summary>
        /// This will pause any matching coroutines running on this MEC instance until ResumeCoroutinesOnInstance is called.
        /// </summary>
        /// <param name="layer">Any coroutines on the matching layer will be paused.</param>
        /// <param name="tag">Any coroutines with a matching tag will be paused.</param>
        public void PauseCoroutinesOnInstance(int layer, string tag)
        {
            if (layer == 0 || tag == null) return;

            List<ProcessIndex> layerMatches;
            if (!_layeredProcesses.TryGetValue(layer, out layerMatches))
                return;

            List<ProcessIndex> tagMatches;
            if (!_taggedProcesses.TryGetValue(tag, out tagMatches))
                return;

            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            WaitingProcess pausedProcs = new WaitingProcess();

            for(int i = 0;i < layerMatches.Count;i++)
            {
                for(int j = 0;j < tagMatches.Count;j++)
                {
                    if(layerMatches[i] == tagMatches[j])
                    {
                        WaitingProcess.ProcessData procData = new WaitingProcess.ProcessData
                        {
                            Segment = layerMatches[i].seg,
                            Layer = RemoveLayer(layerMatches[i]),
                            Tag = RemoveTag(layerMatches[i]),
                            Task = CoindexExtract(layerMatches[i])
                        };

                        SetTimeValues(procData.Segment);
                        if(procData.Task.Current > localTime)
                            procData.PauseTime = procData.Task.Current - localTime;

                        pausedProcs.Tasks.Add(procData);
                    }
                }
            }

            _waitingProcesses.Add(pausedProcs);

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// This resumes all coroutines on the current MEC instance if they are currently paused, otherwise it has
        /// no effect.
        /// </summary>
        public static void ResumeCoroutines()
        {
            if (_instance != null)
                _instance.ResumeCoroutinesOnInstance();
        }

        /// <summary>
        /// This resumes all coroutines on this MEC instance if they are currently paused, otherwise it has no effect.
        /// </summary>
        public void ResumeCoroutinesOnInstance()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                enabled = true;
            }
            else
            {
                _editorPaused = false;
                EditorApplication.update += OnEditorUpdate;
            }
#else
            enabled = true;
#endif
            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].Trigger == null)
                {
                    foreach (var taskData in _waitingProcesses[i].Tasks)
                        RunCoroutineOnInstance(taskData.Task, taskData.Segment, taskData.Layer, taskData.Tag);

                    _waitingProcesses.RemoveAt(i--);
                }
            }

        }

        /// <summary>
        /// This resumes any matching coroutines on the current MEC instance if they are currently paused, otherwise it has
        /// no effect.
        /// </summary>
        /// <param name="tag">Any coroutines previously paused with a matching tag will be resumend.</param>
        public static void ResumeCoroutines(string tag)
        {
            if (_instance != null)
                _instance.ResumeCoroutinesOnInstance(tag);
        }

        /// <summary>
        /// This resumes any matching coroutines on this MEC instance if they are currently paused, otherwise it has no effect.
        /// </summary>
        /// <param name="tag">Any coroutines previously paused with a matching tag will be resumend.</param>
        public void ResumeCoroutinesOnInstance(string tag)
        {
            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if(_waitingProcesses[i].Trigger == null)
                {
                    for(int j = 0;j < _waitingProcesses[i].Tasks.Count;j++)
                    {
                        if(_waitingProcesses[i].Tasks[j].Tag == tag)
                        {
                            WaitingProcess.ProcessData taskData = _waitingProcesses[i].Tasks[j];

                            SetTimeValues(taskData.Segment);

                            RunCoroutineOnInstance(taskData.PauseTime > 0 ? InjectDelay(taskData.Task, localTime + taskData.PauseTime) : taskData.Task, 
                                taskData.Segment, taskData.Layer, taskData.Tag);

                            _waitingProcesses[i].Tasks.RemoveAt(j--);
                        }
                    }

                    if (_waitingProcesses[i].Tasks.Count == 0)
                        _waitingProcesses.RemoveAt(i--);
                }
            }

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// This resumes any matching coroutines on the current MEC instance if they are currently paused, otherwise it has
        /// no effect.
        /// </summary>
        /// <param name="layer">Any coroutines previously paused on the matching layer will be resumend.</param>
        public static void ResumeCoroutines(int layer)
        {
            if (_instance != null)
                _instance.ResumeCoroutinesOnInstance(layer);
        }

        /// <summary>
        /// This resumes any matching coroutines on this MEC instance if they are currently paused, otherwise it has no effect.
        /// </summary>
        /// <param name="layer">Any coroutines previously paused on the matching layer will be resumend.</param>
        public void ResumeCoroutinesOnInstance(int layer)
        {
            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].Trigger == null)
                {
                    for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                    {
                        if (_waitingProcesses[i].Tasks[j].Layer == layer)
                        {
                            WaitingProcess.ProcessData taskData = _waitingProcesses[i].Tasks[j];

                            SetTimeValues(taskData.Segment);

                            RunCoroutineOnInstance(taskData.PauseTime > 0 ? InjectDelay(taskData.Task, localTime + taskData.PauseTime) : taskData.Task,
                                taskData.Segment, taskData.Layer, taskData.Tag);

                            _waitingProcesses[i].Tasks.RemoveAt(j--);
                        }
                    }

                    if (_waitingProcesses[i].Tasks.Count == 0)
                        _waitingProcesses.RemoveAt(i--);
                }
            }

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// This resumes any matching coroutines on the current MEC instance if they are currently paused, otherwise it has
        /// no effect.
        /// </summary>
        /// <param name="layer">Any coroutines previously paused on the matching layer will be resumend.</param>
        /// <param name="tag">Any coroutines previously paused with a matching tag will be resumend.</param>
        public static void ResumeCoroutines(int layer, string tag)
        {
            if (_instance != null)
                _instance.ResumeCoroutinesOnInstance(layer, tag);
        }

        /// <summary>
        /// This resumes any matching coroutines on this MEC instance if they are currently paused, otherwise it has no effect.
        /// </summary>
        /// <param name="layer">Any coroutines previously paused on the matching layer will be resumend.</param>
        /// <param name="tag">Any coroutines previously paused with a matching tag will be resumend.</param>
        public void ResumeCoroutinesOnInstance(int layer, string tag)
        {
            double curLocalTime = localTime;
            float curDeltaTime = deltaTime;

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].Trigger == null)
                {
                    for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                    {
                        if (_waitingProcesses[i].Tasks[j].Layer == layer && _waitingProcesses[i].Tasks[j].Tag == tag)
                        {
                            WaitingProcess.ProcessData taskData = _waitingProcesses[i].Tasks[j];

                            SetTimeValues(taskData.Segment);

                            RunCoroutineOnInstance(taskData.PauseTime > 0 ? InjectDelay(taskData.Task, localTime + taskData.PauseTime) : taskData.Task,
                                taskData.Segment, taskData.Layer, taskData.Tag);

                            _waitingProcesses[i].Tasks.RemoveAt(j--);
                        }
                    }

                    if (_waitingProcesses[i].Tasks.Count == 0)
                        _waitingProcesses.RemoveAt(i--);
                }
            }

            localTime = curLocalTime;
            deltaTime = curDeltaTime;
        }

        /// <summary>
        /// Returns the number of coroutines that are paused under the given tag.
        /// </summary>
        /// <param name="tag">The tag to match.</param>
        /// <returns>The number of matches.</returns>
        public static int CountPausedCoroutines(string tag)
        {
            return _instance == null ? 0 : _instance.CountPausedCoroutinesOnInstance(tag);
        }

        /// <summary>
        /// Returns the number of coroutines that are paused under the given tag.
        /// </summary>
        /// <param name="tag">The tag to match.</param>
        /// <returns>The number of matches.</returns>
        public int CountPausedCoroutinesOnInstance(string tag)
        {
            int count = 0;
            for (int i = 0; i < _waitingProcesses.Count; i++)
                if (_waitingProcesses[i].Trigger == null)
                    for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                        if (_waitingProcesses[i].Tasks[j].Tag == tag)
                            count++;
            
            return count;
        }

        /// <summary>
        /// Returns the number of coroutines that are paused on the given layer.
        /// </summary>
        /// <param name="layer">The layer to match.</param>
        /// <returns>The number of matches.</returns>
        public static int CountPausedCoroutines(int layer)
        {
            return _instance == null ? 0 : _instance.CountPausedCoroutinesOnInstance(layer);
        }

        /// <summary>
        /// Returns the number of coroutines that are paused on the given layer.
        /// </summary>
        /// <param name="layer">The layer to match.</param>
        /// <returns>The number of matches.</returns>
        public int CountPausedCoroutinesOnInstance(int layer)
        {
            int count = 0;
            for (int i = 0; i < _waitingProcesses.Count; i++)
                if (_waitingProcesses[i].Trigger == null)
                    for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                        if (_waitingProcesses[i].Tasks[j].Layer == layer)
                            count++;

            return count;
        }

        /// <summary>
        /// Returns the number of coroutines that are paused under the given tag and layer.
        /// </summary>
        /// <param name="layer">The layer to match.</param>
        /// <param name="tag">The tag to match.</param>
        /// <returns>The number of matches.</returns>
        public static int CountPausedCoroutines(int layer, string tag)
        {
            return _instance == null ? 0 : _instance.CountPausedCoroutinesOnInstance(layer, tag);
        }

        /// <summary>
        /// Returns the number of coroutines that are paused under the given tag and layer.
        /// </summary>
        /// <param name="layer">The layer to match.</param>
        /// <param name="tag">The tag to match.</param>
        /// <returns>The number of matches.</returns>
        public int CountPausedCoroutinesOnInstance(int layer, string tag)
        {
            int count = 0;
            for (int i = 0; i < _waitingProcesses.Count; i++)
                if (_waitingProcesses[i].Trigger == null)
                    for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                        if (_waitingProcesses[i].Tasks[j].Layer == layer && _waitingProcesses[i].Tasks[j].Tag == tag)
                            count++;

            return count;
        }

        private void RemoveUnused()
        {
            ProcessIndex outer, inner;
            outer.seg = inner.seg = Segment.Update;
            for (outer.i = inner.i = 0; outer.i < _nextUpdateProcessSlot; outer.i++)
            {
                if (UpdateProcesses[outer.i] != null)
                {
                    if(outer.i != inner.i)
                    {
                        UpdateProcesses[inner.i] = UpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for(outer.i = inner.i;outer.i < _nextUpdateProcessSlot;outer.i++)
            {
                UpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            UpdateCoroutines = _nextUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.FixedUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextFixedUpdateProcessSlot; outer.i++)
            {
                if(FixedUpdateProcesses[outer.i] != null)
                {
                    if(outer.i != inner.i)
                    {
                        FixedUpdateProcesses[inner.i] = FixedUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for(outer.i = inner.i;outer.i < _nextFixedUpdateProcessSlot;outer.i++)
            {
                FixedUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            FixedUpdateCoroutines = _nextFixedUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.LateUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextLateUpdateProcessSlot; outer.i++)
            {
                if(LateUpdateProcesses[outer.i] != null)
                {
                    if(outer.i != inner.i)
                    {
                        LateUpdateProcesses[inner.i] = LateUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for(outer.i = inner.i;outer.i < _nextLateUpdateProcessSlot;outer.i++)
            {
                LateUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            LateUpdateCoroutines = _nextLateUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.SlowUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextSlowUpdateProcessSlot; outer.i++)
            {
                if (SlowUpdateProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        SlowUpdateProcesses[inner.i] = SlowUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextSlowUpdateProcessSlot; outer.i++)
            { 
                SlowUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            SlowUpdateCoroutines = _nextSlowUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.RealtimeUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextRealtimeUpdateProcessSlot; outer.i++)
            {
                if (RealtimeUpdateProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        RealtimeUpdateProcesses[inner.i] = RealtimeUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextRealtimeUpdateProcessSlot; outer.i++)
            {
                RealtimeUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            RealtimeUpdateCoroutines = _nextRealtimeUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.EndOfFrame;
            for (outer.i = inner.i = 0; outer.i < _nextEndOfFrameProcessSlot; outer.i++)
            {
                if (EndOfFrameProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        EndOfFrameProcesses[inner.i] = EndOfFrameProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextEndOfFrameProcessSlot; outer.i++)
            {
                EndOfFrameProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            EndOfFrameCoroutines = _nextEndOfFrameProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.ManualTimeframe;
            for (outer.i = inner.i = 0; outer.i < _nextManualTimeframeProcessSlot; outer.i++)
            {
                if (ManualTimeframeProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        ManualTimeframeProcesses[inner.i] = ManualTimeframeProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextManualTimeframeProcessSlot; outer.i++)
            {
                ManualTimeframeProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            ManualTimeframeCoroutines = _nextManualTimeframeProcessSlot = inner.i;
        }

        private void EditorRemoveUnused()
        {
            ProcessIndex outer, inner;
            outer.seg = inner.seg = Segment.EditorUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextEditorUpdateProcessSlot; outer.i++)
            {
                if (EditorUpdateProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        EditorUpdateProcesses[inner.i] = EditorUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextEditorUpdateProcessSlot; outer.i++)
            { 
                EditorUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            EditorUpdateCoroutines = _nextEditorUpdateProcessSlot = inner.i;

            outer.seg = inner.seg = Segment.EditorSlowUpdate;
            for (outer.i = inner.i = 0; outer.i < _nextEditorSlowUpdateProcessSlot; outer.i++)
            {
                if (EditorSlowUpdateProcesses[outer.i] != null)
                {
                    if (outer.i != inner.i)
                    {
                        EditorSlowUpdateProcesses[inner.i] = EditorSlowUpdateProcesses[outer.i];
                        MoveGraffiti(outer, inner);
                    }
                    inner.i++;
                }
            }
            for (outer.i = inner.i; outer.i < _nextEditorSlowUpdateProcessSlot; outer.i++)
            { 
                EditorSlowUpdateProcesses[outer.i] = null;
                RemoveGraffiti(outer);
            }

            EditorSlowUpdateCoroutines = _nextEditorSlowUpdateProcessSlot = inner.i;
        }

        private void AddTag(string tag, ProcessIndex coindex)
        {
            _processTags.Add(coindex, tag);

            if(_taggedProcesses.ContainsKey(tag))
                _taggedProcesses[tag].Add(coindex);
            else
                _taggedProcesses.Add(tag, new List<ProcessIndex> { coindex });
        }

        private void AddLayer(int layer, ProcessIndex coindex)
        {
            _processLayers.Add(coindex, layer);

            if (_layeredProcesses.ContainsKey(layer))
                _layeredProcesses[layer].Add(coindex);
            else
                _layeredProcesses.Add(layer, new List<ProcessIndex> { coindex });
        }

        private string RemoveTag(ProcessIndex coindex)
        {
            if (_processTags.ContainsKey(coindex))
            {
                string tag = _processTags[coindex];

                if (_taggedProcesses[tag].Count > 1)
                    _taggedProcesses[tag].Remove(coindex);
                else
                    _taggedProcesses.Remove(tag);

                _processTags.Remove(coindex);

                return tag;
            }

            return null;
        }

        private int RemoveLayer(ProcessIndex coindex)
        {
            if (_processLayers.ContainsKey(coindex))
            {
                int layer = _processLayers[coindex];

                if (_layeredProcesses[layer].Count > 1)
                    _layeredProcesses[layer].Remove(coindex);
                else
                    _layeredProcesses.Remove(layer);

                _processLayers.Remove(coindex);

                return layer;
            }

            return 0;
        }

        private void RemoveGraffiti(ProcessIndex coindex)
        {
            if (_processLayers.ContainsKey(coindex))
            {
                int layer = _processLayers[coindex];

                if (_layeredProcesses[layer].Count > 1)
                    _layeredProcesses[layer].Remove(coindex);
                else
                    _layeredProcesses.Remove(layer);

                _processLayers.Remove(coindex);
            }

            if (_processTags.ContainsKey(coindex))
            {
                string tag = _processTags[coindex];

                if (_taggedProcesses[tag].Count > 1)
                    _taggedProcesses[tag].Remove(coindex);
                else
                    _taggedProcesses.Remove(tag);

                _processTags.Remove(coindex);
            }
        }

        private void MoveGraffiti(ProcessIndex coindexFrom, ProcessIndex coindexTo)
        {
            RemoveGraffiti(coindexTo);

            if (_processLayers.ContainsKey(coindexFrom))
            {
                _layeredProcesses[_processLayers[coindexFrom]].Remove(coindexFrom);
                _layeredProcesses[_processLayers[coindexFrom]].Add(coindexTo);

                _processLayers.Add(coindexTo, _processLayers[coindexFrom]);
                _processLayers.Remove(coindexFrom);
            }

            if (_processTags.ContainsKey(coindexFrom))
            {
                _taggedProcesses[_processTags[coindexFrom]].Remove(coindexFrom);
                _taggedProcesses[_processTags[coindexFrom]].Add(coindexTo);

                _processTags.Add(coindexTo, _processTags[coindexFrom]);
                _processTags.Remove(coindexFrom);
            }
        }

        private IEnumerator<float> FirstLayeredInstance(int layer)
        {
            List<ProcessIndex> matches;
            if (!_layeredProcesses.TryGetValue(layer, out matches))
                return null;

            for (int i = 0; i < matches.Count; i++)
            {
                if (CoindexPeek(matches[i]) != null)
                    return CoindexPeek(matches[i]);
            }

            return null;
        }

        private IEnumerator<float> FirstTaggedInstance(string tag)
        {
            List<ProcessIndex> matches;
            if(!_taggedProcesses.TryGetValue(tag, out matches))
                return null;

            for (int i = 0; i < matches.Count; i++)
            {
                if (CoindexPeek(matches[i]) != null)
                    return CoindexPeek(matches[i]);
            }

            return null;
        }

        private IEnumerator<float> FirstGraffitiedInstance(int layer, string tag)
        {
            List<ProcessIndex> layerMatches;
            List<ProcessIndex> tagMatches;
            if (!_layeredProcesses.TryGetValue(layer, out layerMatches))
                return null;
            if (!_taggedProcesses.TryGetValue(tag, out tagMatches))
                return null;

            for (int i = 0; i < layerMatches.Count; i++)
                for (int j = 0; j < layerMatches.Count; j++)
                    if (layerMatches[i] == tagMatches[j] && CoindexPeek(layerMatches[i]) != null)
                        return CoindexPeek(layerMatches[i]);

            return null;
        }

        private IEnumerator<float> CoindexExtract(ProcessIndex coindex)
        {
            IEnumerator<float> retVal;

            switch (coindex.seg)
            {
                case Segment.Update:
                    retVal = UpdateProcesses[coindex.i];
                    UpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.FixedUpdate:
                    retVal = FixedUpdateProcesses[coindex.i];
                    FixedUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.LateUpdate:
                    retVal = LateUpdateProcesses[coindex.i];
                    LateUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.SlowUpdate:
                    retVal = SlowUpdateProcesses[coindex.i];
                    SlowUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.RealtimeUpdate:
                    retVal = RealtimeUpdateProcesses[coindex.i];
                    RealtimeUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EditorUpdate:
                    retVal = EditorUpdateProcesses[coindex.i];
                    EditorUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EditorSlowUpdate:
                    retVal = EditorSlowUpdateProcesses[coindex.i];
                    EditorSlowUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EndOfFrame:
                    retVal = EndOfFrameProcesses[coindex.i];
                    EndOfFrameProcesses[coindex.i] = null;
                    return retVal;
                case Segment.ManualTimeframe:
                    retVal = ManualTimeframeProcesses[coindex.i];
                    ManualTimeframeProcesses[coindex.i] = null;
                    return retVal;
                default:
                    return null;
            }
        }

        private IEnumerator<float> CoindexPeek(ProcessIndex coindex)
        {
            switch (coindex.seg)
            {
                case Segment.Update:
                    return UpdateProcesses[coindex.i];
                case Segment.FixedUpdate:
                    return FixedUpdateProcesses[coindex.i];
                case Segment.LateUpdate:
                    return LateUpdateProcesses[coindex.i];
                case Segment.SlowUpdate:
                    return SlowUpdateProcesses[coindex.i];
                case Segment.RealtimeUpdate:
                    return RealtimeUpdateProcesses[coindex.i];
                case Segment.EditorUpdate:
                    return EditorUpdateProcesses[coindex.i];
                case Segment.EditorSlowUpdate:
                    return EditorSlowUpdateProcesses[coindex.i];
                case Segment.EndOfFrame:
                    return EndOfFrameProcesses[coindex.i];
                case Segment.ManualTimeframe:
                    return ManualTimeframeProcesses[coindex.i];
                default:
                    return null;
            }
        }

        private bool CoindexKill(ProcessIndex coindex)
        {
            bool retVal;

            switch (coindex.seg)
            {
                case Segment.Update:
                    retVal = UpdateProcesses[coindex.i] != null;
                    UpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.FixedUpdate:
                    retVal = FixedUpdateProcesses[coindex.i] != null;
                    FixedUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.LateUpdate:
                    retVal = LateUpdateProcesses[coindex.i] != null;
                    LateUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.SlowUpdate:
                    retVal = SlowUpdateProcesses[coindex.i] != null;
                    SlowUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.RealtimeUpdate:
                    retVal = RealtimeUpdateProcesses[coindex.i] != null;
                    RealtimeUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EditorUpdate:
                    retVal = UpdateProcesses[coindex.i] != null;
                    EditorUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EditorSlowUpdate:
                    retVal = EditorSlowUpdateProcesses[coindex.i] != null;
                    EditorSlowUpdateProcesses[coindex.i] = null;
                    return retVal;
                case Segment.EndOfFrame:
                    retVal = EndOfFrameProcesses[coindex.i] != null;
                    EndOfFrameProcesses[coindex.i] = null;
                    return retVal;
                case Segment.ManualTimeframe:
                    retVal = ManualTimeframeProcesses[coindex.i] != null;
                    ManualTimeframeProcesses[coindex.i] = null;
                    return retVal;
            }

            return false;
        }

        private bool CoindexMatches(ProcessIndex coindex, IEnumerator<float> handle)
        {
            switch (coindex.seg)
            {
                case Segment.Update:
                    return UpdateProcesses[coindex.i] == handle;
                case Segment.FixedUpdate:
                    return FixedUpdateProcesses[coindex.i] == handle;
                case Segment.LateUpdate:
                    return LateUpdateProcesses[coindex.i] == handle;
                case Segment.SlowUpdate:
                    return SlowUpdateProcesses[coindex.i] == handle;
                case Segment.RealtimeUpdate:
                    return RealtimeUpdateProcesses[coindex.i] == handle;
                case Segment.EditorUpdate:
                    return EditorUpdateProcesses[coindex.i] == handle;
                case Segment.EditorSlowUpdate:
                    return EditorSlowUpdateProcesses[coindex.i] == handle;
                case Segment.EndOfFrame:
                    return EndOfFrameProcesses[coindex.i] == handle;
                case Segment.ManualTimeframe:
                    return ManualTimeframeProcesses[coindex.i] == handle;
                default:
                    return false;
            }
        }

        private bool SetRunning(Segment segment, bool value)
        {
            bool retVal;
            switch (segment)
            {
                case Segment.Update:
                    retVal = _runningUpdate;
                    _runningUpdate = value;
                    return retVal;
                case Segment.FixedUpdate:
                    retVal = _runningFixedUpdate;
                    _runningFixedUpdate = value;
                    return retVal;
                case Segment.LateUpdate:
                    retVal = _runningLateUpdate;
                    _runningLateUpdate = value;
                    return retVal;
                case Segment.SlowUpdate:
                    retVal = _runningSlowUpdate;
                    _runningSlowUpdate = value;
                    return retVal;
                case Segment.RealtimeUpdate:
                    retVal = _runningRealtimeUpdate;
                    _runningRealtimeUpdate = value;
                    return retVal;
                case Segment.EditorUpdate:
                    retVal = _runningEditorUpdate;
                    _runningEditorUpdate = value;
                    return retVal;
                case Segment.EditorSlowUpdate:
                    retVal = _runningEditorSlowUpdate;
                    _runningEditorSlowUpdate = value;
                    return retVal;
                case Segment.ManualTimeframe:
                    retVal = _runningManualTimeframe;
                    _runningManualTimeframe = value;
                    return retVal;
                default:
                    return false;
            }
        }

        private IEnumerator<float> InjectDelay(IEnumerator<float> proc, double returnAt)
        {
            yield return (float)returnAt;

            ReplacementFunction = (input, timing, layer, tag) => proc;
            yield return float.NaN;
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, string tag)
        {
            return coroutine == null ? null : (Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, string tag, bool overwrite)
        {
            if(coroutine == null) return null;
            if (!overwrite) return Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
            KillCoroutines(tag);
            return Instance.RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, int layer)
        {
            return coroutine == null ? null : 
                (Instance.FirstLayeredInstance(layer) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, null));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, int layer, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return Instance.FirstLayeredInstance(layer) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
            KillCoroutines(layer);
            return Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, int layer, string tag)
        {
            return coroutine == null ? null : 
                (Instance.FirstGraffitiedInstance(layer, tag) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, int layer, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
            KillCoroutines(layer, tag);
            return Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, string tag)
        {
            return coroutine == null ? null : (Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, timing, 0, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, timing, 0, tag);
            KillCoroutines(tag);
            return Instance.RunCoroutineOnInstance(coroutine, timing, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, int layer)
        {
            return coroutine == null ? null :
                (Instance.FirstLayeredInstance(layer) ?? Instance.RunCoroutineOnInstance(coroutine, timing, layer, null));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, int layer, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return Instance.FirstLayeredInstance(layer) ?? Instance.RunCoroutineOnInstance(coroutine, timing, layer, null);
            KillCoroutines(layer);
            return Instance.RunCoroutineOnInstance(coroutine, timing, layer, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, int layer, string tag)
        {
            return coroutine == null ? null :
                (Instance.FirstGraffitiedInstance(layer, tag) ?? Instance.RunCoroutineOnInstance(coroutine, timing, layer, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public static IEnumerator<float> RunCoroutineSingleton(IEnumerator<float> coroutine, Segment timing, int layer, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return Instance.FirstTaggedInstance(tag) ?? Instance.RunCoroutineOnInstance(coroutine, timing, layer, tag);
            KillCoroutines(layer, tag);
            return Instance.RunCoroutineOnInstance(coroutine, timing, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, string tag)
        {
            return coroutine == null ? null : (FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
            KillCoroutines(tag);
            return RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, int layer)
        {
            return coroutine == null ? null :
                (FirstLayeredInstance(layer) ?? RunCoroutineOnInstance(coroutine, Segment.Update, layer, null));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, int layer, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstLayeredInstance(layer) ?? RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
            KillCoroutines(layer);
            return RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, int layer, string tag)
        {
            return coroutine == null ? null :
                (FirstGraffitiedInstance(layer, tag) ?? RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, int layer, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
            KillCoroutines(layer, tag);
            return RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, string tag)
        {
            return coroutine == null ? null : (FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, timing, 0, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, timing, 0, tag);
            KillCoroutines(tag);
            return RunCoroutineOnInstance(coroutine, timing, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, int layer)
        {
            return coroutine == null ? null : (FirstLayeredInstance(layer) ?? RunCoroutineOnInstance(coroutine, timing, layer, null));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, int layer, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstLayeredInstance(layer) ?? RunCoroutineOnInstance(coroutine, timing, layer, null);
            KillCoroutines(layer);
            return RunCoroutineOnInstance(coroutine, timing, layer, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, int layer, string tag)
        {
            return coroutine == null ? null : (FirstGraffitiedInstance(layer, tag) ?? RunCoroutineOnInstance(coroutine, timing, layer, tag));
        }

        /// <summary>
        /// Run a new coroutine in the Update segment with the supplied tag unless there is already one or more coroutines running with that tag.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">A layer to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="tag">A tag to attach to the coroutine, and to check for existing instances.</param>
        /// <param name="overwrite">True will kill any pre-existing coroutines. False will only start this coroutine if none exist.
        /// False is the default value.</param>
        /// <returns>The newly created or existing handle.</returns>
        public IEnumerator<float> RunCoroutineSingletonOnInstance(IEnumerator<float> coroutine, Segment timing, int layer, string tag, bool overwrite)
        {
            if (coroutine == null) return null;
            if (!overwrite) return FirstTaggedInstance(tag) ?? RunCoroutineOnInstance(coroutine, timing, layer, tag);
            KillCoroutines(layer, tag);
            return RunCoroutineOnInstance(coroutine, timing, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, Segment.Update, 0, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, string tag)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, int layer)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
        }

        /// <summary>
        /// Run a new coroutine in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, int layer, string tag)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, Segment timing)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, timing, 0, null);
        }

        /// <summary>
        /// Run a new coroutine.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, Segment timing, string tag)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, timing, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, Segment timing, int layer)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, timing, layer, null);
        }

        /// <summary>
        /// Run a new coroutine.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public static IEnumerator<float> RunCoroutine(IEnumerator<float> coroutine, Segment timing, int layer, string tag)
        {
            return coroutine == null ? null : Instance.RunCoroutineOnInstance(coroutine, timing, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine) 
        {
            return RunCoroutineOnInstance(coroutine, Segment.Update, 0, null);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, string tag)
        {
            return RunCoroutineOnInstance(coroutine, Segment.Update, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, int layer)
        {
            return RunCoroutineOnInstance(coroutine, Segment.Update, layer, null);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance in the Update segment.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, int layer, string tag)
        {
            return RunCoroutineOnInstance(coroutine, Segment.Update, layer, tag);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, Segment timing)
        {
            return RunCoroutineOnInstance(coroutine, timing, 0, null);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, Segment timing, string tag)
        {
            return RunCoroutineOnInstance(coroutine, timing, 0, tag);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, Segment timing, int layer)
        {
            return RunCoroutineOnInstance(coroutine, timing, layer, null);
        }

        /// <summary>
        /// Run a new coroutine on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The new coroutine's handle.</param>
        /// <param name="timing">The segment that the coroutine should run in.</param>
        /// <param name="layer">An optional layer to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <param name="tag">An optional tag to attach to the coroutine which can later be used to identify this coroutine.</param>
        /// <returns>The coroutine's handle, which can be used for Wait and Kill operations.</returns>
        public IEnumerator<float> RunCoroutineOnInstance(IEnumerator<float> coroutine, Segment timing, int layer, string tag)
        {
            if(coroutine == null) 
                return null;

            ProcessIndex slot = new ProcessIndex {seg = timing};
            switch(timing)
            {
                case Segment.Update:

                    if(_nextUpdateProcessSlot >= UpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = UpdateProcesses;
                        UpdateProcesses = new IEnumerator<float>[UpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for(int i = 0;i < oldArray.Length;i++)
                            UpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextUpdateProcessSlot++;
                    UpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if(!_runningUpdate)
                    {
                        try
                        {
                            _runningUpdate = true;
                            SetTimeValues(slot.seg);

                            if(!UpdateProcesses[slot.i].MoveNext())
                            {
                                UpdateProcesses[slot.i] = null;
                            }
                            else if (UpdateProcesses[slot.i] != null && float.IsNaN(UpdateProcesses[slot.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    UpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    UpdateProcesses[slot.i] = ReplacementFunction(UpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (UpdateProcesses[slot.i] != null)
                                        UpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            UpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.FixedUpdate:

                    if(_nextFixedUpdateProcessSlot >= FixedUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = FixedUpdateProcesses;
                        FixedUpdateProcesses = new IEnumerator<float>[FixedUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for(int i = 0;i < oldArray.Length;i++)
                            FixedUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextFixedUpdateProcessSlot++;
                    FixedUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if(!_runningFixedUpdate)
                    {
                        try
                        {
                            _runningFixedUpdate = true;
                            SetTimeValues(slot.seg);

                            if(!FixedUpdateProcesses[slot.i].MoveNext())
                            {
                                FixedUpdateProcesses[slot.i] = null;
                            }
                            else if (FixedUpdateProcesses[slot.i] != null && float.IsNaN(FixedUpdateProcesses[slot.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    FixedUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    FixedUpdateProcesses[slot.i] = ReplacementFunction(FixedUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (FixedUpdateProcesses[slot.i] != null)
                                        FixedUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            FixedUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningFixedUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.LateUpdate:

                    if(_nextLateUpdateProcessSlot >= LateUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = LateUpdateProcesses;
                        LateUpdateProcesses = new IEnumerator<float>[LateUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for(int i = 0;i < oldArray.Length;i++)
                            LateUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextLateUpdateProcessSlot++;
                    LateUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if(!_runningLateUpdate)
                    {
                        try
                        {
                            _runningLateUpdate = true;
                            SetTimeValues(slot.seg);

                            if(!LateUpdateProcesses[slot.i].MoveNext())
                            {
                                LateUpdateProcesses[slot.i] = null;
                            }
                            else if (LateUpdateProcesses[slot.i] != null && float.IsNaN(LateUpdateProcesses[slot.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    LateUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    LateUpdateProcesses[slot.i] = ReplacementFunction(LateUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (LateUpdateProcesses[slot.i] != null)
                                        LateUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            LateUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningLateUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.SlowUpdate:

                    if(_nextSlowUpdateProcessSlot >= SlowUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = SlowUpdateProcesses;
                        SlowUpdateProcesses = new IEnumerator<float>[SlowUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for(int i = 0;i < oldArray.Length;i++)
                            SlowUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextSlowUpdateProcessSlot++;
                    SlowUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if(!_runningSlowUpdate)
                    {
                        try
                        {
                            _runningSlowUpdate = true;
                            SetTimeValues(slot.seg);

                            if(!SlowUpdateProcesses[slot.i].MoveNext())
                            {
                                SlowUpdateProcesses[slot.i] = null;
                            }
                            else if (SlowUpdateProcesses[slot.i] != null && float.IsNaN(SlowUpdateProcesses[slot.i].Current))
                            {
                                if(ReplacementFunction == null)
                                {
                                    SlowUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    SlowUpdateProcesses[slot.i] = ReplacementFunction(SlowUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (SlowUpdateProcesses[slot.i] != null)
                                        SlowUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            SlowUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningSlowUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.RealtimeUpdate:

                    if (_nextRealtimeUpdateProcessSlot >= RealtimeUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = RealtimeUpdateProcesses;
                        RealtimeUpdateProcesses = new IEnumerator<float>[RealtimeUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for (int i = 0; i < oldArray.Length; i++)
                            RealtimeUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextRealtimeUpdateProcessSlot++;
                    RealtimeUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if (!_runningRealtimeUpdate)
                    {
                        try
                        {
                            _runningRealtimeUpdate = true;
                            SetTimeValues(slot.seg);

                            if (!RealtimeUpdateProcesses[slot.i].MoveNext())
                            {
                                RealtimeUpdateProcesses[slot.i] = null;
                            }
                            else if (RealtimeUpdateProcesses[slot.i] != null && float.IsNaN(RealtimeUpdateProcesses[slot.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    RealtimeUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    RealtimeUpdateProcesses[slot.i] = ReplacementFunction(RealtimeUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (RealtimeUpdateProcesses[slot.i] != null)
                                        RealtimeUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            RealtimeUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningRealtimeUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.EditorUpdate:

                    if(!OnEditorStart())
                        return null;

                    if (_nextEditorUpdateProcessSlot >= EditorUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = EditorUpdateProcesses;
                        EditorUpdateProcesses = new IEnumerator<float>[EditorUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for (int i = 0; i < oldArray.Length; i++)
                            EditorUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextEditorUpdateProcessSlot++;
                    EditorUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if (!_runningEditorUpdate)
                    {
                        try
                        {
                            _runningEditorUpdate = true;
                            SetTimeValues(slot.seg);

                            if (!EditorUpdateProcesses[slot.i].MoveNext())
                            {
                                EditorUpdateProcesses[slot.i] = null;
                            }
                            else if (EditorUpdateProcesses[slot.i] != null && float.IsNaN(EditorUpdateProcesses[slot.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    EditorUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    EditorUpdateProcesses[slot.i] = ReplacementFunction(EditorUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (EditorUpdateProcesses[slot.i] != null)
                                        EditorUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            EditorUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningEditorUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.EditorSlowUpdate:

                    if(!OnEditorStart())
                        return null;

                    if (_nextEditorSlowUpdateProcessSlot >= EditorSlowUpdateProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = EditorSlowUpdateProcesses;
                        EditorSlowUpdateProcesses = new IEnumerator<float>[EditorSlowUpdateProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for (int i = 0; i < oldArray.Length; i++)
                            EditorSlowUpdateProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextEditorSlowUpdateProcessSlot++;
                    EditorSlowUpdateProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    if (!_runningEditorSlowUpdate)
                    {
                        try
                        {
                            _runningEditorSlowUpdate = true;
                            SetTimeValues(slot.seg);

                            if (!EditorSlowUpdateProcesses[slot.i].MoveNext())
                            {
                                EditorSlowUpdateProcesses[slot.i] = null;
                            }
                            else if (EditorSlowUpdateProcesses[slot.i] != null && float.IsNaN(EditorSlowUpdateProcesses[slot.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    EditorSlowUpdateProcesses[slot.i] = null;
                                }
                                else
                                {
                                    EditorSlowUpdateProcesses[slot.i] = ReplacementFunction(EditorSlowUpdateProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (EditorSlowUpdateProcesses[slot.i] != null)
                                        EditorSlowUpdateProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            EditorSlowUpdateProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningEditorSlowUpdate = false;
                        }
                    }

                    return coroutine;

                case Segment.EndOfFrame:

                    RunCoroutineSingletonOnInstance(_EOFPumpWatcher(), "MEC_EOFPumpWatcher");

                    if (_nextEndOfFrameProcessSlot >= EndOfFrameProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = EndOfFrameProcesses;
                        EndOfFrameProcesses = new IEnumerator<float>[EndOfFrameProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for (int i = 0; i < oldArray.Length; i++)
                            EndOfFrameProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextEndOfFrameProcessSlot++;
                    EndOfFrameProcesses[slot.i] = coroutine;

                    if(null != tag)
                        AddTag(tag, slot);

                    if(0 != layer)
                        AddLayer(layer, slot);

                    return coroutine;

                case Segment.ManualTimeframe:

                    if (_nextManualTimeframeProcessSlot >= ManualTimeframeProcesses.Length)
                    {
                        IEnumerator<float>[] oldArray = ManualTimeframeProcesses;
                        ManualTimeframeProcesses = new IEnumerator<float>[ManualTimeframeProcesses.Length + (ProcessArrayChunkSize * _expansions++)];
                        for (int i = 0; i < oldArray.Length; i++)
                            ManualTimeframeProcesses[i] = oldArray[i];
                    }

                    slot.i = _nextManualTimeframeProcessSlot++;
                    ManualTimeframeProcesses[slot.i] = coroutine;

                    if (null != tag)
                        AddTag(tag, slot);

                    if (0 != layer)
                        AddLayer(layer, slot);

                    if (!_runningManualTimeframe)
                    {
                        try
                        {
                            _runningManualTimeframe = true;
                            SetTimeValues(slot.seg);

                            if (!ManualTimeframeProcesses[slot.i].MoveNext())
                            {
                                ManualTimeframeProcesses[slot.i] = null;
                            }
                            else if (ManualTimeframeProcesses[slot.i] != null && float.IsNaN(ManualTimeframeProcesses[slot.i].Current))
                            {
                                if (ReplacementFunction == null)
                                {
                                    ManualTimeframeProcesses[slot.i] = null;
                                }
                                else
                                {
                                    ManualTimeframeProcesses[slot.i] = ReplacementFunction(ManualTimeframeProcesses[slot.i],
                                        slot.seg, _processLayers.ContainsKey(slot) ? _processLayers[slot] : 0,
                                        _processTags.ContainsKey(slot) ? _processTags[slot] : null);

                                    ReplacementFunction = null;

                                    if (ManualTimeframeProcesses[slot.i] != null)
                                        ManualTimeframeProcesses[slot.i].MoveNext();
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            if (OnError == null)
                                _exceptions.Enqueue(ex);
                            else
                                OnError(ex);

                            ManualTimeframeProcesses[slot.i] = null;
                        }
                        finally
                        {
                            _runningManualTimeframe = false;
                        }
                    }

                    return coroutine;

                default:
                    return null;
            }
        }

        /// <summary>
        /// This will kill all coroutines running on the main MEC instance.
        /// </summary>
        /// <returns>The number of coroutines that were killed.</returns>
        public static void KillAllCoroutines()
        {
            if(_instance != null)
                _instance.KillAllCoroutinesOnInstance();
        }

        /// <summary>
        /// This will kill all coroutines running on the current MEC instance.
        /// </summary>
        /// <returns>The number of coroutines that were killed.</returns>
        public void KillAllCoroutinesOnInstance()
        {
            UpdateProcesses = new IEnumerator<float>[InitialBufferSizeLarge];
            UpdateCoroutines = 0;
            _nextUpdateProcessSlot = 0;

            LateUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            LateUpdateCoroutines = 0;
            _nextLateUpdateProcessSlot = 0;

            FixedUpdateProcesses = new IEnumerator<float>[InitialBufferSizeMedium];
            FixedUpdateCoroutines = 0;
            _nextFixedUpdateProcessSlot = 0;

            SlowUpdateProcesses = new IEnumerator<float>[InitialBufferSizeMedium];
            SlowUpdateCoroutines = 0;
            _nextSlowUpdateProcessSlot = 0;

            RealtimeUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            RealtimeUpdateCoroutines = 0;
            _nextRealtimeUpdateProcessSlot = 0;

            EditorUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            EditorUpdateCoroutines = 0;
            _nextEditorUpdateProcessSlot = 0;

            EditorSlowUpdateProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            EditorSlowUpdateCoroutines = 0;
            _nextEditorSlowUpdateProcessSlot = 0;

            EndOfFrameProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            EndOfFrameCoroutines = 0;
            _nextEndOfFrameProcessSlot = 0;

            ManualTimeframeProcesses = new IEnumerator<float>[InitialBufferSizeSmall];
            ManualTimeframeCoroutines = 0;
            _nextManualTimeframeProcessSlot = 0;

            _processTags.Clear();
            _taggedProcesses.Clear();
            _processLayers.Clear();
            _layeredProcesses.Clear();
            _waitingProcesses.Clear();
            _exceptions.Clear();
            _expansions = (ushort)((_expansions / 2) + 1);

            ResetTimeCountOnInstance();

#if UNITY_EDITOR
            EditorApplication.update -= OnEditorUpdate;
#endif
        }

        /// <summary>
        /// Kills all instances of the coroutine handle on the main Timing instance.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public static int KillCoroutines(IEnumerator<float> coroutine)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(coroutine);
        }

        /// <summary>
        /// Kills all instances of the coroutine handle on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public int KillCoroutinesOnInstance(IEnumerator<float> coroutine)
        {
            int numberFound = 0;

            for (int i = 0; i < _nextUpdateProcessSlot; i++)
            {
                if (UpdateProcesses[i] == coroutine)
                {
                    UpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextFixedUpdateProcessSlot; i++)
            {
                if (FixedUpdateProcesses[i] == coroutine)
                {
                    FixedUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextLateUpdateProcessSlot; i++)
            {
                if (LateUpdateProcesses[i] == coroutine)
                {
                    LateUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextSlowUpdateProcessSlot; i++)
            {
                if (SlowUpdateProcesses[i] == coroutine)
                {
                    SlowUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextRealtimeUpdateProcessSlot; i++)
            {
                if (RealtimeUpdateProcesses[i] == coroutine)
                {
                    RealtimeUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextEditorUpdateProcessSlot; i++)
            {
                if (EditorUpdateProcesses[i] == coroutine)
                {
                    EditorUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextEditorSlowUpdateProcessSlot; i++)
            {
                if (EditorSlowUpdateProcesses[i] == coroutine)
                {
                    EditorSlowUpdateProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _nextEndOfFrameProcessSlot; i++)
            {
                if (EndOfFrameProcesses[i] == coroutine)
                {
                    EndOfFrameProcesses[i] = null;
                    numberFound++;
                }
            }

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if(_waitingProcesses[i].Trigger == coroutine && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if(_waitingProcesses[i].Tasks[j].Task == coroutine && _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Kills all coroutines that have the given tag.
        /// </summary>
        /// <param name="tag">All coroutines with this tag will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public static int KillCoroutines(string tag)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(tag);
        }

        /// <summary> 
        /// Kills all coroutines that have the given tag.
        /// </summary>
        /// <param name="tag">All coroutines with this tag will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public int KillCoroutinesOnInstance(string tag)
        {
            int numberFound = 0;

            if (_taggedProcesses.ContainsKey(tag))
            {
                for(int i = 0;i < _taggedProcesses[tag].Count;i++)
                {
                    if(CoindexKill(_taggedProcesses[tag][i]))
                        numberFound++;

                    _processTags.Remove(_taggedProcesses[tag][i]);
                }
                _taggedProcesses.Remove(tag);
            }

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if(_waitingProcesses[i].TriggerTag == tag && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if(_waitingProcesses[i].Tasks[j].Tag == tag && _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Kills all coroutines on the given layer.
        /// </summary>
        /// <param name="layer">All coroutines on this layer will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public static int KillCoroutines(int layer)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(layer);
        }

        /// <summary> 
        /// Kills all coroutines on the given layer.
        /// </summary>
        /// <param name="layer">All coroutines on this layer will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public int KillCoroutinesOnInstance(int layer)
        {
            int numberFound = 0;

            if (_layeredProcesses.ContainsKey(layer))
            {
                for (int i = 0; i < _layeredProcesses[layer].Count; i++)
                {
                    if (CoindexKill(_layeredProcesses[layer][i]))
                        numberFound++;

                    _processLayers.Remove(_layeredProcesses[layer][i]);
                }
                _layeredProcesses.Remove(layer);
            }

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].TriggerLayer == layer && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if (_waitingProcesses[i].Tasks[j].Layer == layer && _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Kills all coroutines with the given tag on the given layer.
        /// </summary>
        /// <param name="layer">The layer to be killed.</param>
        /// <param name="tag">All coroutines with this tag on the given layer will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public static int KillCoroutines(int layer, string tag)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(layer, tag);
        }

        /// <summary> 
        /// Kills all coroutines with the given tag on the given layer.
        /// </summary>
        /// <param name="layer">The layer to be killed.</param>
        /// <param name="tag">All coroutines with this tag on the given layer will be killed.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        public int KillCoroutinesOnInstance(int layer, string tag)
        {
            int numberFound = 0;

            if (_layeredProcesses.ContainsKey(layer) && _taggedProcesses.ContainsKey(tag))
            {
                List<ProcessIndex> layerList = _layeredProcesses[layer];
                List<ProcessIndex> tagList = _taggedProcesses[tag];

                for (int i = 0; i < layerList.Count; i++)
                {
                    for(int j = 0;j < tagList.Count;j++)
                    {
                        if (layerList[i] == tagList[j])
                        {
                            ProcessIndex match = layerList[i];

                            if (CoindexKill(match))
                                numberFound++;

                            _processLayers.Remove(match);
                            layerList.RemoveAt(i--);
                            _processTags.Remove(match);
                            tagList.RemoveAt(j);

                            break;
                        }
                    }
                }

                if (layerList.Count > 0)
                    _layeredProcesses[layer] = layerList;
                else
                    _layeredProcesses.Remove(layer);

                if (tagList.Count > 0)
                    _taggedProcesses[tag] = tagList;
                else
                    _taggedProcesses.Remove(tag);
            }

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].TriggerLayer == layer && _waitingProcesses[i].TriggerTag == tag && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if (_waitingProcesses[i].Tasks[j].Layer == layer && _waitingProcesses[i].TriggerTag == tag && 
                        _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Kills all instances that match both the coroutine handle and the tag on the main Timing instance.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <param name="tag">The tag to also match for.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public static int KillCoroutines(IEnumerator<float> coroutine, string tag)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(coroutine, tag);
        }

        /// <summary>
        /// Kills all instances that match both the coroutine handle and the tag on this Timing instance.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <param name="tag">The tag to also match for.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public int KillCoroutinesOnInstance(IEnumerator<float> coroutine, string tag)
        {
            int numberFound = 0;

            if (_taggedProcesses.ContainsKey(tag))
            {
                foreach (ProcessIndex coindex in _taggedProcesses[tag])
                {
                    if (CoindexMatches(coindex, coroutine))
                    {
                        if(CoindexKill(coindex))
                            numberFound++;
                        _processTags.Remove(coindex);
                    }
                }
                if (numberFound == _taggedProcesses[tag].Count)
                    _taggedProcesses.Remove(tag);
            }


            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if(_waitingProcesses[i].Trigger == coroutine && _waitingProcesses[i].TriggerTag == tag && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if(_waitingProcesses[i].Tasks[j].Task == coroutine && _waitingProcesses[i].Tasks[j].Tag == tag &&
                       _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Kills the coroutine with the given handle as long as the tag and layer match.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <param name="layer">The layer that must match.</param>
        /// <param name="tag">The tag that must match.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public static int KillCoroutines(IEnumerator<float> coroutine, int layer, string tag)
        {
            return _instance == null ? 0 : _instance.KillCoroutinesOnInstance(coroutine, layer, tag);
        }

        /// <summary> 
        /// Kills the coroutine with the given handle as long as the tag and layer match.
        /// </summary>
        /// <param name="coroutine">The handle of the coroutine to kill.</param>
        /// <param name="layer">The layer that must match.</param>
        /// <param name="tag">The tag that must match.</param>
        /// <returns>The number of coroutines that were found and killed.</returns>
        [System.Obsolete("Please use coroutine tags or layers to identify any coroutines you want to kill.")]
        public int KillCoroutinesOnInstance(IEnumerator<float> coroutine, int layer, string tag)
        {
            int numberFound = 0;

            if (_layeredProcesses.ContainsKey(layer) && _taggedProcesses.ContainsKey(tag))
            {
                List<ProcessIndex> layerList = _layeredProcesses[layer];
                List<ProcessIndex> tagList = _taggedProcesses[tag];

                for (int i = 0; i < layerList.Count; i++)
                {
                    for (int j = 0; j < tagList.Count; j++)
                    {
                        if (layerList[i] == tagList[j] && CoindexMatches(layerList[i], coroutine))
                        {
                            ProcessIndex match = layerList[i];

                            if (CoindexKill(match))
                                numberFound++;

                            _processLayers.Remove(match);
                            layerList.RemoveAt(i--);
                            _processTags.Remove(match);
                            tagList.RemoveAt(j);

                            break;
                        }
                    }
                }

                if (layerList.Count > 0)
                    _layeredProcesses[layer] = layerList;
                else
                    _layeredProcesses.Remove(layer);

                if (tagList.Count > 0)
                    _taggedProcesses[tag] = tagList;
                else
                    _taggedProcesses.Remove(tag);
            }

            for (int i = 0; i < _waitingProcesses.Count; i++)
            {
                if (_waitingProcesses[i].Trigger == coroutine && _waitingProcesses[i].TriggerLayer == layer && 
                    _waitingProcesses[i].TriggerTag == tag && !_waitingProcesses[i].Killed)
                {
                    _waitingProcesses[i].Killed = true;
                    numberFound++;
                }

                for (int j = 0; j < _waitingProcesses[i].Tasks.Count; j++)
                {
                    if (_waitingProcesses[i].Tasks[j].Task == coroutine && _waitingProcesses[i].Tasks[j].Layer == layer && 
                        _waitingProcesses[i].TriggerTag == tag && _waitingProcesses[i].Tasks[j].Task != null)
                    {
                        _waitingProcesses[i].Tasks[j].Task = null;
                        numberFound++;
                    }
                }
            }

            return numberFound;
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(otherCoroutine);" to pause the current 
        /// coroutine until otherCoroutine is done.
        /// </summary>
        /// <param name="otherCoroutine">The coroutine to pause for.</param>
        public static float WaitUntilDone(IEnumerator<float> otherCoroutine)
        {
            return WaitUntilDone(otherCoroutine, true, Instance);
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(otherCoroutine);" to pause the current 
        /// coroutine until otherCoroutine is done.
        /// </summary>
        /// <param name="otherCoroutine">The coroutine to pause for.</param>
        /// <param name="warnOnIssue">Post a warning to the console if no hold action was actually performed.</param>
        public static float WaitUntilDone(IEnumerator<float> otherCoroutine, bool warnOnIssue)
        {
            return WaitUntilDone(otherCoroutine, warnOnIssue, Instance);
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(otherCoroutine);" to pause the current 
        /// coroutine until the otherCoroutine is done.
        /// </summary>
        /// <param name="otherCoroutine">The coroutine to pause for.</param>
        /// <param name="warnOnIssue">Post a warning to the console if no hold action was actually performed.</param>
        /// <param name="instance">The instance that the otherCoroutine is attached to. Only use this if you are using 
        /// multiple instances of the Timing object.</param>
        public static float WaitUntilDone(IEnumerator<float> otherCoroutine, bool warnOnIssue, Timing instance)
        {
            if (instance == null || !instance.gameObject)
                throw new System.ArgumentNullException();

            if(otherCoroutine == null)
            {
                if (warnOnIssue)
                    throw new System.ArgumentNullException();

                return 0f;
            }

            for(int i = 0;i < instance._waitingProcesses.Count;i++)
            {
                if (instance._waitingProcesses[i].Trigger == otherCoroutine)
                {
                    WaitingProcess proc = instance._waitingProcesses[i];
                    ReplacementFunction = (input, segment, layer, tag) =>
                    {
                        proc.Tasks.Add(new WaitingProcess.ProcessData
                        { 
                            Task = input,
                            Layer = layer,
                            Tag = tag,
                            Segment = segment
                        });

                        return null;
                    };

                    return float.NaN;
                }

                for(int j = 0;j < instance._waitingProcesses[i].Tasks.Count;j++)
                {
                    if (instance._waitingProcesses[i].Tasks[j].Task == otherCoroutine)
                    {
                        WaitingProcess proc = new WaitingProcess { Trigger = otherCoroutine };

                        instance._waitingProcesses[i].Tasks[j].Task = instance._StartWhenDone(proc);

                        ReplacementFunction = (input, segment, layer, tag) =>
                        {
                            proc.Tasks.Add(new WaitingProcess.ProcessData
                            {
                                Task = input,
                                Layer = layer,
                                Tag = tag,
                                Segment = segment
                            });

                            instance._waitingProcesses.Add(proc);

                            return null;
                        };

                        return float.NaN;
                    }
                }
            }

            WaitingProcess newProcess = new WaitingProcess { Trigger = otherCoroutine };

            if (instance.ReplaceCoroutine(otherCoroutine, instance._StartWhenDone(newProcess), 
                out newProcess.TriggerLayer, out newProcess.TriggerTag))
            {
                ReplacementFunction = (input, segment, layer, tag) =>
                {
                    newProcess.Tasks.Add(new WaitingProcess.ProcessData
                    {
                        Task = input,
                        Layer = layer,
                        Tag = tag,
                        Segment = segment
                    });

                    instance._waitingProcesses.Add(newProcess);

                    return null;
                };

                return float.NaN;
            }

            if (warnOnIssue)
                Debug.LogWarning("WaitUntilDone cannot hold: The coroutine instance that was passed in was not found.\n" + otherCoroutine);

            return 0f;
        }

        private IEnumerator<float> _StartWhenDone(WaitingProcess processData)
        {
            try
            {
                if(processData.Killed)
                {
                    CloseWaitingProcess(processData);
                    yield break;
                }

                if(processData.Trigger.Current > localTime)
                {
                    yield return processData.Trigger.Current;

                    if(processData.Killed)
                    {
                        CloseWaitingProcess(processData);
                        yield break;
                    }
                }

                while(processData.Trigger.MoveNext())
                {
                    yield return processData.Trigger.Current;

                    if(processData.Killed)
                    {
                        CloseWaitingProcess(processData);
                        yield break;
                    }
                }
            }
            finally
            {
                CloseWaitingProcess(processData);
            }
        }

        private void CloseWaitingProcess(WaitingProcess processData)
        {
            if(_waitingProcesses.Contains(processData))
            {
                _waitingProcesses.Remove(processData);

                foreach(WaitingProcess.ProcessData taskData in processData.Tasks)
                    RunCoroutineOnInstance(taskData.Task, taskData.Segment, taskData.Layer, taskData.Tag);
            }
        }

        private bool ReplaceCoroutine(IEnumerator<float> coroutine, IEnumerator<float> replacement, out int layerFound, out string tagFound)
        {
            ProcessIndex coindex;
            for (coindex.i = 0; coindex.i < _nextUpdateProcessSlot; coindex.i++)
            {
                if (UpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.Update;
                    UpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextFixedUpdateProcessSlot; coindex.i++)
            {
                if (FixedUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.FixedUpdate;
                    FixedUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextLateUpdateProcessSlot; coindex.i++)
            {
                if (LateUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.LateUpdate;
                    LateUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextSlowUpdateProcessSlot; coindex.i++)
            {
                if (SlowUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.SlowUpdate;
                    SlowUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextRealtimeUpdateProcessSlot; coindex.i++)
            {
                if (RealtimeUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.RealtimeUpdate;
                    RealtimeUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextEditorUpdateProcessSlot; coindex.i++)
            {
                if (EditorUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.EditorUpdate;
                    EditorUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextEditorSlowUpdateProcessSlot; coindex.i++)
            {
                if (EditorSlowUpdateProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.EditorSlowUpdate;
                    EditorSlowUpdateProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            for (coindex.i = 0; coindex.i < _nextEndOfFrameProcessSlot; coindex.i++)
            {
                if (EndOfFrameProcesses[coindex.i] == coroutine)
                {
                    coindex.seg = Segment.EndOfFrame;
                    EndOfFrameProcesses[coindex.i] = replacement;
                    tagFound = RemoveTag(coindex);
                    layerFound = RemoveLayer(coindex);

                    return true;
                }
            }

            tagFound = null;
            layerFound = 0;
            return false;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment);" to switch this coroutine to
        /// the given segment on the default instance.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        public static float SwitchCoroutine(Segment newSegment)
        {
            return SwitchCoroutine(newSegment, Instance);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, instance);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="instance">The new instance to run on.</param>
        public static float SwitchCoroutine(Segment newSegment, Timing instance)
        {
            if(instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                bool currentlyRunning = instance.SetRunning(newSegment, true);
                instance.RunCoroutineOnInstance(input, newSegment, layer, tag);
                instance.SetRunning(newSegment, currentlyRunning);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, tag);" to switch this coroutine to
        /// the given values. This will run it on the default instance.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Segment newSegment, string newTag)
        {
            return SwitchCoroutine(newSegment, Instance, newTag);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, instance, tag);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Segment newSegment, Timing instance, string newTag)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                bool currentlyRunning = instance.SetRunning(newSegment, true);
                instance.RunCoroutineOnInstance(input, newSegment, layer, newTag);
                instance.SetRunning(newSegment, currentlyRunning);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, layer);" to switch this coroutine to
        /// the given values. This will run it on the default instance.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        public static float SwitchCoroutine(Segment newSegment, int newLayer)
        {
            return SwitchCoroutine(newSegment, Instance, newLayer);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, instance, layer);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        public static float SwitchCoroutine(Segment newSegment, Timing instance, int newLayer)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                bool currentlyRunning = instance.SetRunning(newSegment, true);
                instance.RunCoroutineOnInstance(input, newSegment, newLayer, tag);
                instance.SetRunning(newSegment, currentlyRunning);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, layer, tag);" to switch this coroutine to
        /// the given values. This will run it on the default instance.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Segment newSegment, int newLayer, string newTag)
        {
            return SwitchCoroutine(newSegment, Instance, newLayer, newTag);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(segment, instance, layer, tag);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="newSegment">The new segment to run in.</param>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Segment newSegment, Timing instance, int newLayer, string newTag)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                bool currentlyRunning = instance.SetRunning(newSegment, true);
                instance.RunCoroutineOnInstance(input, newSegment, newLayer, newTag);
                instance.SetRunning(newSegment, currentlyRunning);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(tag);" to switch this coroutine to
        /// the given tag on the default instance.
        /// </summary>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(string newTag)
        {
            return SwitchCoroutine(Instance, newTag);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(instance, tag);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Timing instance, string newTag)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                instance.RunCoroutineOnInstance(input, timing, layer, newTag);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(layer);" to switch this coroutine to
        /// the given tag on the default instance.
        /// </summary>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        public static float SwitchCoroutine(int newLayer)
        {
            return SwitchCoroutine(Instance, newLayer);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(instance, layer);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        public static float SwitchCoroutine(Timing instance, int newLayer)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                instance.RunCoroutineOnInstance(input, timing, newLayer, tag);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(layer, tag);" to switch this coroutine to
        /// the given tag on the default instance.
        /// </summary>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(int newLayer, string newTag)
        {
            return SwitchCoroutine(Instance, newLayer, newTag);
        }

        /// <summary>
        /// Use the command "yield return Timing.SwitchCoroutine(instance, layer, tag);" to switch this coroutine to
        /// the given values.
        /// </summary>
        /// <param name="instance">The new instance to run on.</param>
        /// <param name="newLayer">The new layer to apply, or 0 to remove this coroutine's layer.</param>
        /// <param name="newTag">The new tag to apply, or null to remove this coroutine's tag.</param>
        public static float SwitchCoroutine(Timing instance, int newLayer, string newTag)
        {
            if (instance == null) return 0f;
            ReplacementFunction = (input, timing, layer, tag) =>
            {
                instance.RunCoroutineOnInstance(input, timing, newLayer, newTag);
                return null;
            };
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(wwwObject);" to pause the current 
        /// coroutine until the wwwObject is done.
        /// </summary>
        /// <param name="wwwObject">The www object to pause for.</param>
        public static float WaitUntilDone(WWW wwwObject)
        {
            if(wwwObject == null || wwwObject.isDone) return 0f;
            ReplacementFunction = (input, timing, layer, tag) => _StartWhenDone(wwwObject, input);
        
            return float.NaN;
        }

        private static IEnumerator<float> _StartWhenDone(WWW wwwObject, IEnumerator<float> pausedProc)
        {
            while (!wwwObject.isDone)
                yield return 0f;

            ReplacementFunction = delegate { return pausedProc; };
            yield return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(operation);" to pause the current 
        /// coroutine until the operation is done.
        /// </summary>
        /// <param name="operation">The operation variable returned.</param>
        public static float WaitUntilDone(AsyncOperation operation)
        {
            if (operation == null || operation.isDone) return 0f;
            ReplacementFunction = (input, timing, layer, tag) => _StartWhenDone(operation, input);
            return float.NaN;
        }

        private static IEnumerator<float> _StartWhenDone(AsyncOperation operation, IEnumerator<float> pausedProc)
        {
            while (!operation.isDone)
                yield return 0f;

            ReplacementFunction = delegate { return pausedProc; };
            yield return float.NaN;
        }

#if !UNITY_4_6 && !UNITY_4_7 && !UNITY_5_0 && !UNITY_5_1 && !UNITY_5_2
        /// <summary>
        /// Use the command "yield return Timing.WaitUntilDone(operation);" to pause the current 
        /// coroutine until the operation is done.
        /// </summary>
        /// <param name="operation">The operation variable returned.</param>
        public static float WaitUntilDone(CustomYieldInstruction operation)
        {
            if (operation == null || !operation.keepWaiting) return 0f;
            ReplacementFunction = (input, timing, layer, tag) => _StartWhenDone(operation, input);
            return float.NaN;
        }

        private static IEnumerator<float> _StartWhenDone(CustomYieldInstruction operation, IEnumerator<float> pausedProc)
        {
            while (operation.keepWaiting)
                yield return 0f;

            ReplacementFunction = delegate { return pausedProc; };
            yield return float.NaN;
        }
#endif

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilTrue(evaluatorFunc);" to pause the current 
        /// coroutine until the evaluator function returns true.
        /// </summary>
        /// <param name="evaluatorFunc">The evaluator function.</param>
        public static float WaitUntilTrue(System.Func<bool> evaluatorFunc)
        {
            if (evaluatorFunc == null || evaluatorFunc()) return 0f;
            ReplacementFunction = (input, timing, layer, tag) => _StartWhenDone(evaluatorFunc, false, input);
            return float.NaN;
        }

        /// <summary>
        /// Use the command "yield return Timing.WaitUntilFalse(evaluatorFunc);" to pause the current 
        /// coroutine until the evaluator function returns false.
        /// </summary>
        /// <param name="evaluatorFunc">The evaluator function.</param>
        public static float WaitUntilFalse(System.Func<bool> evaluatorFunc)
        {
            if (evaluatorFunc == null || !evaluatorFunc()) return 0f;
            ReplacementFunction = (input, timing, layer, tag) => _StartWhenDone(evaluatorFunc, true, input);
            return float.NaN;
        }

        private static IEnumerator<float> _StartWhenDone(System.Func<bool> evaluatorFunc, bool continueOn, IEnumerator<float> pausedProc)
        {
            while (evaluatorFunc() == continueOn)
                yield return 0f;

            ReplacementFunction = delegate { return pausedProc; };
            yield return float.NaN;
        }



        /// <summary>
        /// Use in a yield return statement to wait for the specified number of seconds.
        /// </summary>
        /// <param name="waitTime">Number of seconds to wait.</param>
        public static float WaitForSeconds(float waitTime)
        {
            if (float.IsNaN(waitTime)) waitTime = 0f;
            return LocalTime + waitTime;
        }

        /// <summary>
        /// Use in a yield return statement to wait for the specified number of seconds.
        /// </summary>
        /// <param name="waitTime">Number of seconds to wait.</param>
        public float WaitForSecondsOnInstance(float waitTime)
        {
            if (float.IsNaN(waitTime)) waitTime = 0f;
            return (float)localTime + waitTime;
        }

        /// <summary>
        /// Calls the specified action after a specified number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action.</param>
        /// <param name="delay">The number of seconds to wait before calling the action.</param>
        /// <param name="action">The action to call.</param>
        public static void CallDelayed<TRef>(TRef reference, float delay, System.Action<TRef> action)
        {
            if (action == null) return;

            if (delay >= -0.001f)
                RunCoroutine(Instance._CallDelayBack(reference, delay, action));
            else
                action(reference);
        }

        /// <summary>
        /// Calls the specified action after a specified number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action.</param>
        /// <param name="delay">The number of seconds to wait before calling the action.</param>
        /// <param name="action">The action to call.</param>
        public void CallDelayedOnInstance<TRef>(TRef reference, float delay, System.Action<TRef> action)
        {
            if(action == null) return;

            if (delay >= -0.001f)
                RunCoroutineOnInstance(_CallDelayBack(reference, delay, action));
            else
                action(reference);
        }

        private IEnumerator<float> _CallDelayBack<TRef>(TRef reference, float delay, System.Action<TRef> action)
        {
            yield return (float)localTime + delay;

            CallDelayed(reference, -1f, action);
        }

        /// <summary>
        /// Calls the specified action after a specified number of seconds.
        /// </summary>
        /// <param name="delay">The number of seconds to wait before calling the action.</param>
        /// <param name="action">The action to call.</param>
        public static void CallDelayed(float delay, System.Action action)
        {
            if (action == null) return;

            if (delay >= -0.0001f)
                RunCoroutine(Instance._CallDelayBack(delay, action));
            else
                action();
        }

        /// <summary>
        /// Calls the specified action after a specified number of seconds.
        /// </summary>
        /// <param name="delay">The number of seconds to wait before calling the action.</param>
        /// <param name="action">The action to call.</param>
        public void CallDelayedOnInstance(float delay, System.Action action)
        {
            if (action == null) return;

            if (delay >= -0.0001f)
                RunCoroutineOnInstance(_CallDelayBack(delay, action));
            else
                action();
        }

        private IEnumerator<float> _CallDelayBack(float delay, System.Action action)
        {
            yield return (float)localTime + delay;

            CallDelayed(-1f, action);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallPeriodically(float timeframe, float period, System.Action action, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutine(Instance._CallContinuously(timeframe, period, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallPeriodicallyOnInstance(float timeframe, float period, System.Action action, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(timeframe, period, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallPeriodically(float timeframe, float period, System.Action action, Segment timing, System.Action onDone = null)
        {
            if(action != null)
                RunCoroutine(Instance._CallContinuously(timeframe, period, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallPeriodicallyOnInstance(float timeframe, float period, System.Action action, Segment timing, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(timeframe, period, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallContinuously(float timeframe, System.Action action, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutine(Instance._CallContinuously(timeframe, 0f, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallContinuouslyOnInstance(float timeframe, System.Action action, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(timeframe, 0f, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallContinuously(float timeframe, System.Action action, Segment timing, System.Action onDone = null)
        {
            if(action != null)
                RunCoroutine(Instance._CallContinuously(timeframe, 0f, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallContinuouslyOnInstance(float timeframe, System.Action action, Segment timing, System.Action onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(timeframe, 0f, action, onDone), timing);
        }

        private IEnumerator<float> _CallContinuously(float timeframe, float period, System.Action action, System.Action onDone)
        {
            double startTime = localTime;
            while (localTime <= startTime + timeframe)
            {
                yield return WaitForSeconds(period);

                action();
            }

            if (onDone != null)
                onDone();
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each period.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallPeriodically<T>(T reference, float timeframe, float period, System.Action<T> action, System.Action<T> onDone = null)
        {
            if (action != null)
                RunCoroutine(Instance._CallContinuously(reference, timeframe, period, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each period.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallPeriodicallyOnInstance<T>(T reference, float timeframe, float period, System.Action<T> action, System.Action<T> onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(reference, timeframe, period, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each period.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallPeriodically<T>(T reference, float timeframe, float period, System.Action<T> action, 
            Segment timing, System.Action<T> onDone = null)
        {
            if(action != null)
                RunCoroutine(Instance._CallContinuously(reference, timeframe, period, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action at the given rate for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each period.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="period">The amount of time between calls.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallPeriodicallyOnInstance<T>(T reference, float timeframe, float period, System.Action<T> action,
            Segment timing, System.Action<T> onDone = null)
        {
            if(action != null)
                RunCoroutineOnInstance(_CallContinuously(reference, timeframe, period, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each frame.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallContinuously<T>(T reference, float timeframe, System.Action<T> action, System.Action<T> onDone = null)
        {
            if(action != null)
                RunCoroutine(Instance._CallContinuously(reference, timeframe, 0f, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each frame.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallContinuouslyOnInstance<T>(T reference, float timeframe, System.Action<T> action, System.Action<T> onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(reference, timeframe, 0f, action, onDone), Segment.Update);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each frame.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public static void CallContinuously<T>(T reference, float timeframe, System.Action<T> action, 
            Segment timing, System.Action<T> onDone = null)
        {
            if(action != null)
                RunCoroutine(Instance._CallContinuously(reference, timeframe, 0f, action, onDone), timing);
        }

        /// <summary>
        /// Calls the supplied action every frame for a given number of seconds.
        /// </summary>
        /// <param name="reference">A value that will be passed in to the supplied action each frame.</param>
        /// <param name="timeframe">The number of seconds that this function should run.</param>
        /// <param name="action">The action to call every frame.</param>
        /// <param name="timing">The timing segment to run in.</param>
        /// <param name="onDone">An optional action to call when this function finishes.</param>
        public void CallContinuouslyOnInstance<T>(T reference, float timeframe, System.Action<T> action,
            Segment timing, System.Action<T> onDone = null)
        {
            if (action != null)
                RunCoroutineOnInstance(_CallContinuously(reference, timeframe, 0f, action, onDone), timing);
        }

        private IEnumerator<float> _CallContinuously<T>(T reference, float timeframe, float period,
            System.Action<T> action, System.Action<T> onDone = null)
        {
            double startTime = localTime;
            while (localTime <= startTime + timeframe)
            {
                yield return WaitForSeconds(period);

                action(reference);
            }

            if (onDone != null)
                onDone(reference);
        }


        [System.Obsolete("Unity coroutine function, use RunCoroutine instead.", true)]
        public new Coroutine StartCoroutine(System.Collections.IEnumerator routine) { return null; }

        [System.Obsolete("Unity coroutine function, use RunCoroutine instead.", true)]
        public new Coroutine StartCoroutine(string methodName, object value) { return null; }

        [System.Obsolete("Unity coroutine function, use RunCoroutine instead.", true)]
        public new Coroutine StartCoroutine(string methodName) { return null; }

        [System.Obsolete("Unity coroutine function, use RunCoroutine instead.", true)]
        public new Coroutine StartCoroutine_Auto(System.Collections.IEnumerator routine) { return null; }

        [System.Obsolete("Unity coroutine function, use KillCoroutine instead.", true)]
        public new void StopCoroutine(string methodName) {}

        [System.Obsolete("Unity coroutine function, use KillCoroutine instead.", true)]
        public new void StopCoroutine(System.Collections.IEnumerator routine) {}

        [System.Obsolete("Unity coroutine function, use KillCoroutine instead.", true)]
        public new void StopCoroutine(Coroutine routine) {}

        [System.Obsolete("Unity coroutine function, use KillAllCoroutines instead.", true)]
        public new void StopAllCoroutines() {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void Destroy(Object obj) {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void Destroy(Object obj, float f) {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void DestroyObject(Object obj) {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void DestroyObject(Object obj, float f) {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void DestroyImmediate(Object obj) {}

        [System.Obsolete("Use your own GameObject for this.", true)]
        public new static void DestroyImmediate(Object obj, bool b) {}

        [System.Obsolete("Just.. no.", true)]
        public new static T FindObjectOfType<T>() where T : Object { return null; }

        [System.Obsolete("Just.. no.", true)]
        public new static Object FindObjectOfType(System.Type t) { return null; }

        [System.Obsolete("Just.. no.", true)]
        public new static T[] FindObjectsOfType<T>() where T : Object { return null; }

        [System.Obsolete("Just.. no.", true)]
        public new static Object[] FindObjectsOfType(System.Type t) { return null; }

        [System.Obsolete("Just.. no.", true)]
        public new static void print(object message) {}
    }

    public static class MECExtensionMethods
    {
        /// <summary>
        /// Adds a delay to the beginning of this coroutine.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="timeToDelay">The number of second to delay this coroutine.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Delay(this IEnumerator<float> coroutine, float timeToDelay)
        {
            yield return Timing.WaitForSeconds(timeToDelay);

            while (coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Adds a delay to the beginning of this coroutine until a function returns true.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="condition">The coroutine will be paused until this function returns true.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Delay(this IEnumerator<float> coroutine, System.Func<bool> condition)
        {
            while(!condition())
                yield return 0f;

            while (coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Cancels this coroutine when the supplied game object is destroyed or made inactive.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="gameObject">The GameObject to test.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> CancelWith(this IEnumerator<float> coroutine, GameObject gameObject)
        {
            while (gameObject && gameObject.activeInHierarchy && coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Cancels this coroutine when the supplied game objects are destroyed or made inactive.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="gameObject1">The first GameObject to test.</param>
        /// <param name="gameObject2">The second GameObject to test</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> CancelWith(this IEnumerator<float> coroutine, GameObject gameObject1, GameObject gameObject2)
        {
            while (gameObject1 && gameObject1.activeInHierarchy && gameObject2 && gameObject2.activeInHierarchy && coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Cancels this coroutine when the supplied game objects are destroyed or made inactive.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="gameObject1">The first GameObject to test.</param>
        /// <param name="gameObject2">The second GameObject to test</param>
        /// <param name="gameObject3">The third GameObject to test.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> CancelWith(this IEnumerator<float> coroutine, 
            GameObject gameObject1, GameObject gameObject2, GameObject gameObject3)
        {
            while (gameObject1 && gameObject1.activeInHierarchy && gameObject2 && gameObject2.activeInHierarchy && 
                    gameObject3 && gameObject3.activeInHierarchy && coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Cancels this coroutine when the supplied function returns false.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="condition">The test function. True for continue, false to stop.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> CancelWith(this IEnumerator<float> coroutine, System.Func<bool> condition)
        {
            if(condition == null) yield break;

            while (condition() && coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Runs the supplied coroutine immediately after this one.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="nextCoroutine">The coroutine to run next.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Append(this IEnumerator<float> coroutine, IEnumerator<float> nextCoroutine)
        {
            while (coroutine.MoveNext())
                yield return coroutine.Current;

            if(nextCoroutine != null)
                while (nextCoroutine.MoveNext())
                    yield return nextCoroutine.Current;
        }

        /// <summary>
        /// Runs the supplied function immediately after this coroutine finishes.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="onDone">The action to run after this coroutine finishes.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Append(this IEnumerator<float> coroutine, System.Action onDone)
        {
            while (coroutine.MoveNext())
                yield return coroutine.Current;

            if(onDone != null)
                onDone();
        }

        /// <summary>
        /// Runs the supplied coroutine immediately before this one.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="lastCoroutine">The coroutine to run first.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Prepend(this IEnumerator<float> coroutine, IEnumerator<float> lastCoroutine)
        {
            if(lastCoroutine != null)
                while (lastCoroutine.MoveNext())
                    yield return lastCoroutine.Current;

            while (coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Runs the supplied function immediately before this coroutine starts.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="onStart">The action to run before this coroutine starts.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Prepend(this IEnumerator<float> coroutine, System.Action onStart)
        {
            if(onStart != null)
                onStart();

            while (coroutine.MoveNext())
                yield return coroutine.Current;
        }

        /// <summary>
        /// Combines the this coroutine with another and runs them in a combined handle.
        /// </summary>
        /// <param name="coroutineA">The coroutine handle to act upon.</param>
        /// <param name="coroutineB">The coroutine handle to combine.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Superimpose(this IEnumerator<float> coroutineA, IEnumerator<float> coroutineB)
        {
            return Superimpose(coroutineA, coroutineB, Timing.Instance);
        }

        /// <summary>
        /// Combines the this coroutine with another and runs them in a combined handle.
        /// </summary>
        /// <param name="coroutineA">The coroutine handle to act upon.</param>
        /// <param name="coroutineB">The coroutine handle to combine.</param>
        /// <param name="instance">The timing instance that this will be run in, if not the default instance.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Superimpose(this IEnumerator<float> coroutineA, IEnumerator<float> coroutineB, Timing instance)
        {
            while(coroutineA != null || coroutineB != null)
            {
                if (coroutineA != null && !(instance.localTime < coroutineA.Current) && !coroutineA.MoveNext()) 
                    coroutineA = null;

                if (coroutineB != null && !(instance.localTime < coroutineB.Current) && !coroutineB.MoveNext()) 
                    coroutineB = null;

                if ((coroutineA != null && float.IsNaN(coroutineA.Current)) || (coroutineB != null && float.IsNaN(coroutineB.Current)))
                    yield return float.NaN;
                else if (coroutineA != null && coroutineB != null)
                    yield return coroutineA.Current < coroutineB.Current ? coroutineA.Current : coroutineB.Current;
                else if (coroutineA == null && coroutineB != null)
                    yield return coroutineB.Current;
                else if (coroutineA != null)
                    yield return coroutineA.Current;
            }
        }

        /// <summary>
        /// Uses the passed in function to change the return values of this coroutine.
        /// </summary>
        /// <param name="coroutine">The coroutine handle to act upon.</param>
        /// <param name="newReturn">A function that takes the current return value and returns the new return.</param>
        /// <returns>The modified coroutine handle.</returns>
        public static IEnumerator<float> Hijack(this IEnumerator<float> coroutine, System.Func<float, float> newReturn) 
        {
            if(newReturn == null) yield break;

            while(coroutine.MoveNext())
                yield return newReturn(coroutine.Current);
        }
    }

    public enum Segment
    {
        Update,
        FixedUpdate,
        LateUpdate,
        SlowUpdate,
        RealtimeUpdate,
        EditorUpdate,
        EditorSlowUpdate,
        EndOfFrame,
        ManualTimeframe
    }
}
