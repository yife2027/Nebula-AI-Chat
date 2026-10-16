using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NebulaAPI;
using NebulaModel.DataStructures.Chat;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NebulaAIChat
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(NebulaModAPI.NEBULA_MODID, BepInDependency.DependencyFlags.HardDependency)]
    public class AIChatPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yourname.nebula.aichat";
        public const string PluginName = "Nebula AI Chat";
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log;
        internal static AIChatPlugin Instance;

        private static readonly HttpClient httpClient = new HttpClient();

        internal static ConfigEntry<string> ApiUrl;
        internal static ConfigEntry<string> ApiKey;
        internal static ConfigEntry<string> ModelName;
        internal static ConfigEntry<bool> IsEnabled;
        internal static ConfigEntry<int> MaxReplyLength;
        internal static ConfigEntry<string> SystemPrompt;

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ApiUrl = Config.Bind("API", "Url", "https://apihub.agnes-ai.com/v1/chat/completions",
                "AI API 地址（OpenAI 兼容格式）");
            ApiKey = Config.Bind("API", "Key", "", "你的 API Key（仅客户端需要）");
            ModelName = Config.Bind("API", "Model", "agnes-2.5-flash", "模型名称");
            SystemPrompt = Config.Bind("API", "SystemPrompt",
                "你是《戴森球计划》游戏助手。回答简短幽默，50字内。" +
                "工具调用规则：" +
                "问位置/电量/星球用 get_player_status；" +
                "问星球建筑或敌人用 get_planet_info；" +
                "问科技进度用 get_research_status；" +
                "问背包物品用 get_inventory；" +
                "问戴森球用 get_dyson_sphere_summary；" +
                "遇到物品/科技/配方的数字 ID，必须先用 translate_name 翻译成中文名再回答。" +
                "步骤：先分析问题；涉及游戏状态必须调工具；拿到返回 JSON 后转成自然语言回答；" +
                "物品或科技 ID 不要直接说数字，必须先翻译；工具出错就说读不到不要编造；无关问题直接答。回答里不要贴 JSON。",
                "系统提示词");
            MaxReplyLength = Config.Bind("General", "MaxReplyLength", 200, "AI 回复最大长度");
            IsEnabled = Config.Bind("General", "Enabled", true, "是否启用 AI 聊天");

            httpClient.Timeout = TimeSpan.FromSeconds(60);

            // 主线程创建 MainThreadDispatcher
            try
            {
                var dispatcherGo = new GameObject("NebulaAIChat_MainThreadDispatcher");
                DontDestroyOnLoad(dispatcherGo);
                dispatcherGo.AddComponent<Utils.MainThreadDispatcher>();
                Log.LogInfo("[AI] MainThreadDispatcher 已创建。");
            }
            catch (Exception ex)
            {
                Log.LogError($"[AI] MainThreadDispatcher 创建失败: {ex}");
            }

            // 安装聊天补丁
            harmony = new Harmony(PluginGuid);
            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("[AI] Harmony 补丁安装完成。");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[AI] PatchAll 部分失败: {ex.Message}");
            }

            Patches.ChatPatch.TryInstall(harmony);

            Log.LogInfo($"[AI] {PluginName} v{PluginVersion} 已加载。");
        }

        private void OnDestroy()
        {
            try { harmony?.UnpatchSelf(); } catch { }
        }

        // ------------------------------------------------------------------
        // 聊天输入处理
        // ------------------------------------------------------------------

        public static bool OnChatMessageReceived(string username, string message)
        {
            if (!IsEnabled.Value) return false;
            if (string.IsNullOrEmpty(message)) return false;
            if (!message.StartsWith("@ai ", StringComparison.OrdinalIgnoreCase)) return false;

            string prompt = message.Substring(4).Trim();
            if (string.IsNullOrEmpty(prompt)) return true;

            Log?.LogInfo($"[AI] 请求 [{username}]: {prompt}");
            ProcessAIRequest(username, prompt);
            return true;
        }

        // ------------------------------------------------------------------
        // 调用 AI 并根据成功/失败选择公开或本地显示
        // ------------------------------------------------------------------

        public static void ProcessAIRequest(string username, string prompt)
        {
            Task.Run(async () =>
            {
                string reply;
                bool isError;
                try
                {
                    Log?.LogInfo("[AI] >>> 开始调用 API");
                    (reply, isError) = await CallAIAsync(prompt);
                    Log?.LogInfo($"[AI] >>> CallAIAsync 返回 (isError={isError}): {reply}");
                }
                catch (Exception ex)
                {
                    Log?.LogError($"[AI] >>> CallAIAsync 未捕获异常: {ex}");
                    reply = "（AI 请求异常）";
                    isError = true;
                }

                int max = MaxReplyLength.Value;
                if (!string.IsNullOrEmpty(reply) && reply.Length > max)
                    reply = reply.Substring(0, max) + "...";

                try
                {
                    Utils.MainThreadDispatcher.Enqueue(() =>
                    {
                        try
                        {
                            string text = $"[AI 回复 {username}]: {reply}";
                            Log?.LogMessage(text);

                            if (isError)
                            {
                                TryShowInGameChatLocal(text);
                            }
                            else
                            {
                                TryShowInGameChat(text);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log?.LogError($"[AI] 显示失败: {ex}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log?.LogError($"[AI] 调度到主线程失败: {ex}");
                }
            });
        }

        // ------------------------------------------------------------------
        // 公开广播（成功时）
        // ------------------------------------------------------------------

        private static void TryShowInGameChat(string text)
        {
            try
            {
                var chatManagerType = AccessTools.TypeByName(
                    "NebulaWorld.MonoBehaviours.Local.Chat.ChatManager");
                if (chatManagerType == null)
                {
                    Log?.LogWarning($"[AI] 未找到 ChatManager，消息: {text}");
                    return;
                }

                var instanceField = AccessTools.Field(chatManagerType, "Instance");
                var instance = instanceField?.GetValue(null);
                if (instance == null)
                {
                    Log?.LogWarning($"[AI] ChatManager.Instance 为 null，消息: {text}");
                    return;
                }

                var submitMethod = AccessTools.Method(chatManagerType, "OnMessageSubmitted",
                    new[] { typeof(string) });
                if (submitMethod == null)
                {
                    Log?.LogWarning($"[AI] 未找到 OnMessageSubmitted(string)，消息: {text}");
                    return;
                }

                submitMethod.Invoke(instance, new object[] { text });
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[AI] 显示聊天消息失败: {ex.Message} | {text}");
            }
        }

        // ------------------------------------------------------------------
        // 本地显示（失败时）
        // ------------------------------------------------------------------

        private static void TryShowInGameChatLocal(string text)
        {
            try
            {
                var chatManagerType = AccessTools.TypeByName(
                    "NebulaWorld.MonoBehaviours.Local.Chat.ChatManager");
                if (chatManagerType == null)
                {
                    Log?.LogWarning($"[AI] 未找到 ChatManager，消息: {text}");
                    return;
                }

                var instanceField = AccessTools.Field(chatManagerType, "Instance");
                var instance = instanceField?.GetValue(null);
                if (instance == null)
                {
                    Log?.LogWarning($"[AI] ChatManager.Instance 为 null，消息: {text}");
                    return;
                }

                var sendMethod = AccessTools.Method(chatManagerType, "SendChatMessage",
                    new[] { typeof(string), typeof(ChatMessageType) });
                if (sendMethod == null)
                {
                    Log?.LogWarning($"[AI] 未找到 SendChatMessage(string, ChatMessageType)，消息: {text}");
                    return;
                }

                sendMethod.Invoke(instance, new object[]
                {
                    text,
                    ChatMessageType.CommandErrorMessage
                });
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[AI] 本地显示失败: {ex.Message} | {text}");
            }
        }

        // ------------------------------------------------------------------
        // 清洗 AI 返回：去掉 <think> 和 <tool_call>
        // ------------------------------------------------------------------

        private static string CleanReply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"<think>.*?</think>", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"<tool_call>.*?</tool_call>", "",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        // ------------------------------------------------------------------
        // HTTP 请求（带 tools / tool_calls 工具调用）
        // ------------------------------------------------------------------

        private static async Task<(string Text, bool IsError)> CallAIAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            try
            {
                if (string.IsNullOrEmpty(ApiKey.Value))
                    return ("（AI 未配置 API Key）", true);

                var messages = new List<object>
                {
                    new { role = "system", content = SystemPrompt.Value },
                    new { role = "user", content = prompt }
                };

                int maxRounds = 3;

                for (int round = 0; round < maxRounds; round++)
                {
                    var body = new JObject
                    {
                        ["model"] = ModelName.Value,
                        ["messages"] = JArray.FromObject(messages),
                        ["tools"] = Utils.GameDataTools.Definitions,
                        ["tool_choice"] = "auto",
                        ["max_tokens"] = 500,
                        ["temperature"] = 0.7
                    };

                    string json = body.ToString(Formatting.None);
                    Log?.LogInfo($"[AI] 请求体: {json}");

                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl.Value);
                    req.Content = content;
                    req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey.Value}");

                    using var resp = await httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
                    string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Log?.LogInfo($"[AI] 原始响应: {respText}");

                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        Log?.LogError($"[AI] API 错误 {code}: {respText}");
                        return ($"（AI 请求失败：HTTP {code}）", true);
                    }

                    var root = JObject.Parse(respText);
                    var choice = root["choices"]?[0];
                    var message = choice?["message"];
                    var finishReason = choice?["finish_reason"]?.ToString();
                    Log?.LogInfo($"[AI] finish_reason = {finishReason}");

                    var toolCalls = message?["tool_calls"] as JArray;
                    if (toolCalls != null && toolCalls.Count > 0)
                    {
                        Log?.LogInfo($"[AI] AI 请求调用 {toolCalls.Count} 个工具（第 {round + 1} 轮）");

                        messages.Add(new
                        {
                            role = "assistant",
                            content = (string)null,
                            tool_calls = toolCalls
                        });

                        foreach (var call in toolCalls)
                        {
                            string callId = call["id"]?.ToString();
                            string funcName = call["function"]?["name"]?.ToString();
                            string argsJson = call["function"]?["arguments"]?.ToString() ?? "{}";

                            Log?.LogInfo($"[AI] 执行工具 {funcName}({argsJson})");

                            string result = await Utils.GameDataTools
                                .ExecuteAsync(funcName, argsJson)
                                .ConfigureAwait(false);

                            messages.Add(new
                            {
                                role = "tool",
                                tool_call_id = callId,
                                content = result
                            });
                        }

                        continue;
                    }

                    var text = message?["content"]?.ToString()?.Trim();
                    text = CleanReply(text);
                    if (string.IsNullOrEmpty(text))
                        return ("（AI 返回内容为空）", true);
                    return (text, false);
                }

                return ("（AI 工具调用次数超限）", true);
            }
            catch (OperationCanceledException)
            {
                Log?.LogError("[AI] API 请求超时（45 秒）");
                return ("（AI 请求超时，请稍后再试）", true);
            }
            catch (HttpRequestException hex)
            {
                Log?.LogError($"[AI] HTTP 请求异常: {hex}");
                return ("（AI 网络异常，无法连接服务器）", true);
            }
            catch (Exception ex)
            {
                Log?.LogError($"[AI] 未知异常: {ex}");
                return ("（AI 请求失败，请检查日志）", true);
            }
        }
    }
}