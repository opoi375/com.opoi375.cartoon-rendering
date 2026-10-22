// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using System.Reflection;
using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 快捷键读取：同时兼容旧的 Input Manager 与新的 Input System。
    ///
    /// 为什么要这么写：
    ///   * 工程切到 Input System 之后，<c>UnityEngine.Input.GetKeyDown</c> 会直接抛
    ///     <c>InvalidOperationException</c>。而它是在 <c>Update</c> 里被调用的，
    ///     异常会把后面的自动演示 / 动画推进一起中断 —— 表现就是"什么都没发生"。
    ///   * 又不想让这个包对 <c>com.unity.inputsystem</c> 产生硬依赖（asmdef 里写死
    ///     引用的话，没装该包的工程会直接编译不过）。所以新输入系统这条路走反射，
    ///     只取 <c>Keyboard.current[Key].wasPressedThisFrame</c>，一次缓存、每帧两次查表。
    /// </summary>
    internal static class ScanInput
    {
        /// <summary>这一帧是否按下了 <paramref name="keyCode"/>。</summary>
        public static bool WasPressedThisFrame(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
                return false;

#if ENABLE_LEGACY_INPUT_MANAGER
            // 旧输入系统可用就直接用，最便宜
            return Input.GetKeyDown(keyCode);
#elif ENABLE_INPUT_SYSTEM
            return InputSystemBackend.WasPressed(keyCode);
#else
            return false;
#endif
        }

        /// <summary>
        /// 运行时有没有任何可用的键盘输入后端。用于在 Inspector 上给个提示。
        /// </summary>
        public static bool HasKeyboard
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return true;
#elif ENABLE_INPUT_SYSTEM
                return InputSystemBackend.HasKeyboard;
#else
                return false;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        /// <summary>纯反射访问 InputSystem，避免 asmdef 硬引用。</summary>
        private static class InputSystemBackend
        {
            private static bool s_Resolved;
            private static PropertyInfo s_CurrentProp;     // Keyboard.current
            private static PropertyInfo s_Indexer;         // Keyboard[Key]
            private static PropertyInfo s_WasPressed;      // ButtonControl.wasPressedThisFrame
            private static PropertyInfo s_AnyKeyProp;      // Keyboard.anyKey
            private static readonly System.Collections.Generic.Dictionary<KeyCode, object> s_KeyCache =
                new System.Collections.Generic.Dictionary<KeyCode, object>();

            private static void Resolve()
            {
                if (s_Resolved)
                    return;
                s_Resolved = true;

                try
                {
                    var keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                    var keyType      = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
                    var buttonType   = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem");
                    if (keyboardType == null || keyType == null || buttonType == null)
                        return;

                    s_CurrentProp = keyboardType.GetProperty("current",
                        BindingFlags.Public | BindingFlags.Static);
                    s_Indexer = keyboardType.GetProperty("Item", new[] { keyType });
                    s_WasPressed = buttonType.GetProperty("wasPressedThisFrame");
                    s_AnyKeyProp = keyboardType.GetProperty("anyKey",
                        BindingFlags.Public | BindingFlags.Instance);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[ScanEffect] 反射访问 Input System 失败，快捷键不可用：" + e.Message);
                }
            }

            public static bool HasKeyboard
            {
                get
                {
                    Resolve();
                    if (s_CurrentProp == null || s_AnyKeyProp == null)
                        return false;
                    var kb = s_CurrentProp.GetValue(null);
                    return kb != null && (bool)s_AnyKeyProp.GetValue(kb);
                }
            }

            public static bool WasPressed(KeyCode keyCode)
            {
                Resolve();
                if (s_CurrentProp == null || s_Indexer == null || s_WasPressed == null)
                    return false;

                var kb = s_CurrentProp.GetValue(null);
                if (kb == null)
                    return false;   // 没有键盘（触屏 / 手柄）不报错，静默返回

                if (!s_KeyCache.TryGetValue(keyCode, out var keyValue))
                {
                    // KeyCode.F5 → Key.F5（两者枚举名一致）；手写映射没意义
                    var keyType = s_Indexer.GetIndexParameters()[0].ParameterType;
                    if (!Enum.TryParse(keyType, keyCode.ToString(), out keyValue))
                    {
                        s_KeyCache[keyCode] = null;
                        return false;
                    }
                    s_KeyCache[keyCode] = keyValue;
                }

                if (keyValue == null)
                    return false;

                var control = s_Indexer.GetValue(kb, new[] { keyValue });
                return control != null && (bool)s_WasPressed.GetValue(control);
            }
        }
#endif
    }
}
