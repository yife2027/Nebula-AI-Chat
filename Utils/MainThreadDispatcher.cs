using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace NebulaAIChat.Utils
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher instance;
        private readonly ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();

        private void Awake()
        {
            instance = this;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;

            if (instance == null)
            {
                AIChatPlugin.Log?.LogWarning("[AI] MainThreadDispatcher 未初始化，消息被丢弃");
                return;
            }

            instance.actions.Enqueue(action);
        }

        /// <summary>
        /// 在主线程执行 Func，并同步等待结果。可以从后台线程 await。
        /// </summary>
        public static System.Threading.Tasks.Task<T> InvokeAsync<T>(Func<T> func)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<T>();
            Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        private void Update()
        {
            while (actions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex)
                {
                    AIChatPlugin.Log?.LogError($"[AI] 主线程执行异常: {ex}");
                }
            }
        }
    }
}