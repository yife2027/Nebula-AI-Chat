using System;
using System.Reflection;
using HarmonyLib;
using NebulaAIChat;

namespace NebulaAIChat.Patches
{
    /// <summary>
    /// 拦截 Nebula 的聊天输入。
    ///
    /// Nebula 真实入口：
    ///   NebulaWorld.MonoBehaviours.Local.Chat.ChatManager.OnMessageSubmitted(string input)
    ///
    /// 行为：
    ///   - 对 "@ai " 开头的消息：触发 AI 请求，同时返回 true 让 Nebula 正常广播（公开可见）
    ///   - 对其他消息：直接放行
    /// </summary>
    public static class ChatPatch
    {
        public static void TryInstall(Harmony harmony)
        {
            try
            {
                var managerType = AccessTools.TypeByName(
                    "NebulaWorld.MonoBehaviours.Local.Chat.ChatManager");
                if (managerType == null)
                {
                    AIChatPlugin.Log?.LogWarning("[AI] 未找到 ChatManager 类型。");
                    return;
                }

                var target = AccessTools.Method(managerType, "OnMessageSubmitted",
                    new[] { typeof(string) });
                if (target == null)
                {
                    AIChatPlugin.Log?.LogWarning("[AI] 未找到 OnMessageSubmitted(string)。");
                    return;
                }

                var prefix = new HarmonyMethod(typeof(ChatPatch)
                    .GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.Public));

                harmony.Patch(target, prefix: prefix);
                AIChatPlugin.Log?.LogInfo(
                    $"[AI] 聊天补丁已安装: {target.DeclaringType?.FullName}.{target.Name}(string)");
            }
            catch (Exception ex)
            {
                AIChatPlugin.Log?.LogError($"[AI] 聊天补丁安装异常: {ex}");
            }
        }

        /// <summary>
        /// __0 表示原方法的第 0 个参数（string input）。
        /// 返回 true —— 原方法继续执行，消息被 Nebula 正常广播。
        /// </summary>
        public static bool Prefix(string __0)
        {
            try
            {
                string input = __0;
                if (string.IsNullOrEmpty(input))
                    return true;

                if (!input.StartsWith("@ai ", StringComparison.OrdinalIgnoreCase))
                    return true;

                string username = ResolveUsername();
                AIChatPlugin.OnChatMessageReceived(username, input);
                return true; // 放行，让 Nebula 正常广播原始消息
            }
            catch (Exception ex)
            {
                AIChatPlugin.Log?.LogError($"[AI] ChatPatch.Prefix 异常: {ex}");
                return true;
            }
        }

        /// <summary>
        /// 获取玩家名。优先用 Nebula 的 LocalPlayer.Data.Username；
        /// 拿不到则退回游戏本体账号名。
        /// 全用反射，不直接引用 NebulaAPI 类型，避免版本不匹配。
        /// </summary>
        private static string ResolveUsername()
        {
            try
            {
                var nebulaModApiType = AccessTools.TypeByName("NebulaAPI.NebulaModAPI");
                if (nebulaModApiType != null)
                {
                    var sessionProp = AccessTools.Property(nebulaModApiType, "MultiplayerSession");
                    var session = sessionProp?.GetValue(null);
                    if (session != null)
                    {
                        var localPlayerProp = AccessTools.Property(session.GetType(), "LocalPlayer");
                        var localPlayer = localPlayerProp?.GetValue(session);
                        if (localPlayer != null)
                        {
                            var dataProp = AccessTools.Property(localPlayer.GetType(), "Data");
                            var data = dataProp?.GetValue(localPlayer);
                            if (data != null)
                            {
                                var usernameProp = AccessTools.Property(data.GetType(), "Username");
                                var username = usernameProp?.GetValue(data) as string;
                                if (!string.IsNullOrEmpty(username)) return username;
                            }
                        }
                    }
                }
            }
            catch { }

            // 回退：游戏本体账号名
            try
            {
                var data = GameMain.data;
                if (data != null)
                {
                    var name = data.account.userName;
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }

            return "Player";
        }
    }
}