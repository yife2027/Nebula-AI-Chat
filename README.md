# Nebula AI Chat

为 [Nebula 多人联机 Mod](https://github.com/NebulaModTeam/nebula) 添加 AI 聊天助手。

在游戏聊天框输入 `@ai 你的问题`，AI 会读取实时游戏状态（位置、机甲、科技、背包、戴森球等）并公开回复。

![version](https://img.shields.io/badge/version-1.5.0-blue)
![license](https://img.shields.io/badge/license-MIT-green)

---

## ✨ 功能

- **@ai 聊天指令**：在聊天框输入 `@ai 问题`，AI 自动回复
- **实时游戏数据**：AI 可以读取当前星球、坐标、机甲电量/HP、科技进度、背包物品、戴森球概况
- **公开回复**：`@ai` 原始消息和 AI 回复都公开显示，所有玩家可见
- **错误本地显示**：AI 请求超时或报错时，只有发问者能看到错误，不打扰其他人
- **无需服务端安装**：只需客户端装插件即可，服务端 vanilla Nebula 也能连
- **Function Calling**：使用 OpenAI 兼容的工具调用协议，支持多轮工具调用

---

## 📦 安装

### 前置要求

- 戴森球计划 **0.10.35.29104**
- BepInEx **5.4.x**
- Nebula Multiplayer Mod **0.9.22-dev.8**（或更高）

### 安装步骤

1. 下载 `Nebula-AI.dll`
2. 放入游戏目录：
