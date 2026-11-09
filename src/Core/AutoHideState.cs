using System;

namespace LiteMonitor.src.Core
{
    public enum DockEdge { None, Left, Right, Top, Bottom }

    public enum HideActionKind { None, Hide, Show }

    /// <summary>屏幕工作区（不依赖 System.Drawing，便于在纯 net10.0 测试项目中引用）。</summary>
    public readonly struct ScreenRect
    {
        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public ScreenRect(int left, int top, int right, int bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }
    }

    /// <summary>窗口矩形。</summary>
    public readonly struct WindowRect
    {
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => Left + Width;
        public int Bottom => Top + Height;

        public WindowRect(int left, int top, int width, int height)
        {
            Left = left; Top = top; Width = width; Height = height;
        }

        public bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>一次 tick 的决策结果；NewLeft / NewTop 为 null 表示对应坐标不动。</summary>
    public readonly struct HideDecision
    {
        public HideActionKind Kind { get; }
        public DockEdge Edge { get; }
        public int? NewLeft { get; }
        public int? NewTop { get; }

        private HideDecision(HideActionKind kind, DockEdge edge, int? newLeft, int? newTop)
        {
            Kind = kind; Edge = edge; NewLeft = newLeft; NewTop = newTop;
        }

        public static HideDecision NoAction() => new(HideActionKind.None, DockEdge.None, null, null);
        public static HideDecision DoHide(DockEdge edge, int? left, int? top) => new(HideActionKind.Hide, edge, left, top);
        public static HideDecision DoShow(DockEdge edge, int? left, int? top) => new(HideActionKind.Show, edge, left, top);
    }

    /// <summary>
    /// 靠边自动隐藏的纯状态机。不引用 WinForms / System.Drawing，
    /// Screen / Cursor 等环境数据由调用方传入，便于回归测试覆盖各种时序。
    ///
    /// 修复的既有问题：
    ///  1. 拖拽标志一旦丢失 MouseUp 就永久为 true，此后所有 tick 直接返回 → 用超时 + 显式 BeginDrag/EndDrag 兜底；
    ///  2. 位置被外部钳制/恢复后状态未重置 → 提供 ResetAfterRelocate 统一复位；
    ///  3. 隐藏后窗口中心越界，Screen.FromPoint 会切到另一块屏 → 隐藏时锁定 DockArea；
    ///  4. 保护期用 DateTime.Now（可被系统校时影响）→ 改用 Environment.TickCount64 单调时钟；
    ///  5. 只支持左/右/上三边 → 补 Bottom。
    /// </summary>
    public sealed class AutoHideState
    {
        public const int HideWidth = 4;
        public const int HideThreshold = 10;
        public const int HoverBand = 30;
        public const long DragTimeoutMs = 5000;

        private readonly Func<long> _clock;
        private long _keepVisibleUntil;
        private long _dragStartedAt;

        public AutoHideState(Func<long>? clock = null)
        {
            _clock = clock ?? (() => Environment.TickCount64);
            _keepVisibleUntil = long.MinValue;
            _dragStartedAt = long.MinValue;
        }

        public bool IsHidden { get; private set; }
        public bool IsDragging { get; private set; }
        public DockEdge Dock { get; private set; }

        /// <summary>隐藏时锁定的屏幕工作区，避免窗口中心越界后基准屏幕漂移。</summary>
        public ScreenRect? DockArea { get; private set; }

        public void BeginDrag()
        {
            IsDragging = true;
            _dragStartedAt = _clock();
        }

        public void EndDrag() => IsDragging = false;

        public void KeepVisible(double seconds)
        {
            _keepVisibleUntil = _clock() + (long)Math.Ceiling(seconds * 1000);
        }

        /// <summary>强制显示。返回是否原来处于隐藏态（调用方据此把窗口拉回屏幕）。</summary>
        public bool ForceShow()
        {
            bool wasHidden = IsHidden;
            ResetHiddenState();
            KeepVisible(3.0);
            return wasHidden;
        }

        /// <summary>
        /// 位置被 RestorePos / 显示器变化 / 布局切换改动后调用：
        /// 无论走的是「找到原屏幕」还是「降级」分支，都必须重置隐藏状态，
        /// 否则窗口已经完整可见、_isHidden 却仍为 true，隐藏分支会被永久挡住。
        /// </summary>
        public void ResetAfterRelocate(double visibleSeconds = 0.5)
        {
            ResetHiddenState();
            KeepVisible(visibleSeconds);
        }

        private void ResetHiddenState()
        {
            IsHidden = false;
            Dock = DockEdge.None;
            DockArea = null;
        }

        /// <summary>拖拽超过 5 秒仍未收到 EndDrag，认为 MouseUp 丢失，自动解锁。</summary>
        private void ExpireStaleDrag()
        {
            if (IsDragging && _clock() - _dragStartedAt >= DragTimeoutMs)
                EndDrag();
        }

        /// <summary>
        /// 计算一次 250ms tick 的动作。调用方负责把 NewLeft / NewTop 应用到窗口。
        /// </summary>
        public HideDecision Evaluate(
            bool enabled,
            bool formVisible,
            bool menuOpen,
            ScreenRect area,
            WindowRect bounds,
            int cursorX,
            int cursorY)
        {
            ExpireStaleDrag();

            if (!enabled || !formVisible || menuOpen || IsDragging || _clock() < _keepVisibleUntil)
                return HideDecision.NoAction();

            // 隐藏态一律使用当时锁定的工作区，不再依赖可能漂移的 Screen.FromPoint 结果。
            ScreenRect effective = IsHidden && DockArea.HasValue ? DockArea.Value : area;

            if (!IsHidden)
                return EvaluateHide(effective, bounds, cursorX, cursorY);

            return EvaluateShow(effective, bounds, cursorX, cursorY);
        }

        private HideDecision EvaluateHide(ScreenRect area, WindowRect bounds, int cursorX, int cursorY)
        {
            bool nearLeft = bounds.Left <= area.Left + HideThreshold;
            bool nearRight = area.Right - bounds.Right <= HideThreshold;
            bool nearTop = bounds.Top <= area.Top + HideThreshold;
            bool nearBottom = area.Bottom - bounds.Bottom <= HideThreshold;

            if (!(nearLeft || nearRight || nearTop || nearBottom)) return HideDecision.NoAction();

            // 鼠标仍压在窗口矩形内时不隐藏（上游行为）；贴边后需把鼠标移开。
            if (bounds.Contains(cursorX, cursorY)) return HideDecision.NoAction();

            DockEdge edge;
            int? newLeft = null;
            int? newTop = null;

            if (nearRight)
            {
                edge = DockEdge.Right;
                newLeft = area.Right - HideWidth;
            }
            else if (nearLeft)
            {
                edge = DockEdge.Left;
                newLeft = area.Left - (bounds.Width - HideWidth);
            }
            else if (nearTop)
            {
                edge = DockEdge.Top;
                newTop = area.Top - (bounds.Height - HideWidth);
            }
            else
            {
                edge = DockEdge.Bottom;
                newTop = area.Bottom - HideWidth;
            }

            IsHidden = true;
            Dock = edge;
            DockArea = area;
            return HideDecision.DoHide(edge, newLeft, newTop);
        }

        private HideDecision EvaluateShow(ScreenRect area, WindowRect bounds, int cursorX, int cursorY)
        {
            bool onHiddenPanel;
            bool inHoverBand;

            switch (Dock)
            {
                case DockEdge.Right:
                    onHiddenPanel = cursorX >= area.Right - HideWidth &&
                                    cursorY >= bounds.Top && cursorY <= bounds.Bottom;
                    inHoverBand = cursorX >= area.Right - HoverBand;
                    break;
                case DockEdge.Left:
                    onHiddenPanel = cursorX <= area.Left + HideWidth &&
                                    cursorY >= bounds.Top && cursorY <= bounds.Bottom;
                    inHoverBand = cursorX <= area.Left + HoverBand;
                    break;
                case DockEdge.Top:
                    onHiddenPanel = cursorY <= area.Top + HideWidth &&
                                    cursorX >= bounds.Left && cursorX <= bounds.Right;
                    inHoverBand = cursorY <= area.Top + HoverBand;
                    break;
                case DockEdge.Bottom:
                    onHiddenPanel = cursorY >= area.Bottom - HideWidth &&
                                    cursorX >= bounds.Left && cursorX <= bounds.Right;
                    inHoverBand = cursorY >= area.Bottom - HoverBand;
                    break;
                default:
                    return HideDecision.NoAction();
            }

            if (!onHiddenPanel || !inHoverBand) return HideDecision.NoAction();

            int? newLeft = Dock switch
            {
                DockEdge.Right => area.Right - bounds.Width,
                DockEdge.Left => area.Left,
                _ => (int?)null
            };
            int? newTop = Dock switch
            {
                DockEdge.Top => area.Top,
                DockEdge.Bottom => area.Bottom - bounds.Height,
                _ => (int?)null
            };

            DockEdge edge = Dock;
            ResetHiddenState();
            KeepVisible(0.3); // 弹出后给鼠标一点时间移入窗口，避免立刻又被判定隐藏
            return HideDecision.DoShow(edge, newLeft, newTop);
        }
    }
}
