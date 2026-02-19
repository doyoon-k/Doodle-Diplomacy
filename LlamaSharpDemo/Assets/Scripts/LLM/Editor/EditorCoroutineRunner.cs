using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Minimal coroutine runner that executes IEnumerator routines inside the Unity editor (without play mode).
/// </summary>
[InitializeOnLoad]
public static class EditorCoroutineRunner
{
    private static readonly List<EditorCoroutineInstance> ActiveRoutines = new();

    static EditorCoroutineRunner()
    {
        EditorApplication.update += Update;
    }

    public static void Start(IEnumerator routine)
    {
        if (routine == null)
        {
            return;
        }

        ActiveRoutines.Add(new EditorCoroutineInstance(routine));
    }

    private static void Update()
    {
        for (int i = ActiveRoutines.Count - 1; i >= 0; i--)
        {
            if (!ActiveRoutines[i].MoveNext())
            {
                ActiveRoutines.RemoveAt(i);
            }
        }
    }

    private class EditorCoroutineInstance
    {
        private readonly Stack<IEnumerator> _stack = new();
        private double? _waitUntil;
        private AsyncOperation _pendingAsyncOperation;

        public EditorCoroutineInstance(IEnumerator root)
        {
            _stack.Push(root);
        }

        public bool MoveNext()
        {
            if (_waitUntil.HasValue)
            {
                if (EditorApplication.timeSinceStartup < _waitUntil.Value)
                {
                    return true;
                }

                _waitUntil = null;
            }

            if (_pendingAsyncOperation != null)
            {
                if (!_pendingAsyncOperation.isDone)
                {
                    return true;
                }

                _pendingAsyncOperation = null;
            }

            while (_stack.Count > 0)
            {
                var enumerator = _stack.Peek();
                bool moved = enumerator.MoveNext();
                if (!moved)
                {
                    _stack.Pop();
                    continue;
                }

                object current = enumerator.Current;
                if (current == null)
                {
                    return true;
                }

                if (current is IEnumerator nested)
                {
                    _stack.Push(nested);
                    return true;
                }

                if (current is WaitForSeconds wait)
                {
                    _waitUntil = EditorApplication.timeSinceStartup + ResolveWaitTime(wait);
                    return true;
                }

                if (current is AsyncOperation asyncOp)
                {
                    _pendingAsyncOperation = asyncOp;
                    return true;
                }

                // Unsupported instructions act as a single frame delay.
                return true;
            }

            return false;
        }
    }

    private static readonly FieldInfo WaitForSecondsField =
        typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.NonPublic | BindingFlags.Instance);

    private static double ResolveWaitTime(WaitForSeconds wait)
    {
        if (WaitForSecondsField != null)
        {
            object value = WaitForSecondsField.GetValue(wait);
            if (value is float seconds)
            {
                return seconds;
            }
        }

        return 0d;
    }
}
