// SPDX-License-Identifier: GPL-3.0-or-later
using Gradiwin.Core.Models;

namespace Gradiwin.Core.Algorithms;

public enum CropHit
{
    None,
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft,
    Top,
    Right,
    Bottom,
    Left,
    Area,
}

/// <summary>
/// Port of overlay/crop_overlay.py interaction math. All pointer coordinates are in view pixels;
/// the image is displayed at (ImgX, ImgY, ImgW, ImgH).
/// </summary>
public sealed class CropModel
{
    public const double HandleSize = 30;
    public const double EdgeGrabDistance = 8;
    public const double MinDimension = 0.2;

    private CropHit _drag;
    private double _startX, _startY;
    private CropRect _startCrop;

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; } = 1;
    public double Height { get; private set; } = 1;

    /// <summary>Locked aspect ratio (width/height in pixels); 0 = free.</summary>
    public double AspectRatio { get; private set; }

    public CropRect Rect => new(X, Y, Width, Height);

    public bool HasCrop => Width != 1 || Height != 1;

    public bool IsDragging => _drag != CropHit.None;

    public void Set(CropRect rect)
    {
        (X, Y, Width, Height) = (rect.X, rect.Y, rect.Width, rect.Height);
        Clamp();
    }

    public void SetAspectRatio(double ratio, double imgW, double imgH)
    {
        AspectRatio = ratio;
        ApplyAspectRatio(imgW, imgH);
    }

    public void ApplyAspectRatio(double imgW, double imgH)
    {
        if (AspectRatio <= 0 || imgW <= 0 || imgH <= 0)
            return;
        double target = AspectRatio * imgH / imgW;
        double current = Width / Height;
        if (Math.Abs(target - current) < 1e-5)
            return;
        double cx = X + Width / 2, cy = Y + Height / 2;
        if (Width / target > Height)
        {
            double nw = Height * target;
            X = cx - nw / 2;
            Width = nw;
        }
        else
        {
            double nh = Width / target;
            Y = cy - nh / 2;
            Height = nh;
        }
        Clamp();
    }

    public CropHit HitTest(double x, double y, double imgX, double imgY, double imgW, double imgH)
    {
        double cx = imgX + X * imgW, cy = imgY + Y * imgH, cw = Width * imgW, ch = Height * imgH;
        double half = HandleSize / 2;

        (CropHit, double, double)[] corners =
        [
            (CropHit.TopLeft, cx, cy),
            (CropHit.TopRight, cx + cw, cy),
            (CropHit.BottomRight, cx + cw, cy + ch),
            (CropHit.BottomLeft, cx, cy + ch),
        ];
        foreach (var (hit, hx, hy) in corners)
            if (Math.Abs(x - hx) <= half && Math.Abs(y - hy) <= half)
                return hit;

        if (AspectRatio <= 0)
        {
            (CropHit, double, double)[] edges =
            [
                (CropHit.Top, cx + cw / 2, cy),
                (CropHit.Right, cx + cw, cy + ch / 2),
                (CropHit.Bottom, cx + cw / 2, cy + ch),
                (CropHit.Left, cx, cy + ch / 2),
            ];
            foreach (var (hit, hx, hy) in edges)
                if (Math.Abs(x - hx) <= half && Math.Abs(y - hy) <= half)
                    return hit;
        }

        double g = EdgeGrabDistance;
        if (x >= cx && x <= cx + cw && Math.Abs(y - cy) <= g) return CropHit.Top;
        if (x >= cx && x <= cx + cw && Math.Abs(y - (cy + ch)) <= g) return CropHit.Bottom;
        if (y >= cy && y <= cy + ch && Math.Abs(x - cx) <= g) return CropHit.Left;
        if (y >= cy && y <= cy + ch && Math.Abs(x - (cx + cw)) <= g) return CropHit.Right;

        if (x >= cx && x <= cx + cw && y >= cy && y <= cy + ch)
            return CropHit.Area;
        return CropHit.None;
    }

    public bool BeginDrag(double x, double y, double imgX, double imgY, double imgW, double imgH)
    {
        _drag = HitTest(x, y, imgX, imgY, imgW, imgH);
        _startX = x;
        _startY = y;
        _startCrop = Rect;
        return _drag != CropHit.None;
    }

    public void UpdateDrag(double x, double y, double imgW, double imgH)
    {
        if (_drag == CropHit.None || imgW <= 0 || imgH <= 0)
            return;
        double dx = (x - _startX) / imgW, dy = (y - _startY) / imgH;
        var (sx, sy, sw, sh) = (_startCrop.X, _startCrop.Y, _startCrop.Width, _startCrop.Height);

        if (_drag == CropHit.Area)
        {
            X = sx + dx;
            Y = sy + dy;
            Clamp();
            return;
        }

        bool locked = AspectRatio > 0;
        double target = locked ? AspectRatio * imgH / imgW : 0;
        double centerX = sx + sw / 2, centerY = sy + sh / 2;

        switch (_drag)
        {
            case CropHit.TopLeft when locked:
                { double nw = sw - dx, nh = nw / target; (X, Y, Width, Height) = (sx + sw - nw, sy + sh - nh, nw, nh); break; }
            case CropHit.TopRight when locked:
                { double nw = sw + dx, nh = nw / target; (X, Y, Width, Height) = (sx, sy + sh - nh, nw, nh); break; }
            case CropHit.BottomLeft when locked:
                { double nw = sw - dx, nh = nw / target; (X, Y, Width, Height) = (sx + sw - nw, sy, nw, nh); break; }
            case CropHit.BottomRight when locked:
                { double nw = sw + dx, nh = nw / target; (Width, Height) = (nw, nh); break; }
            case CropHit.Top when locked:
                { double nh = sh - dy, nw = nh * target; (X, Y, Width, Height) = (centerX - nw / 2, sy + dy, nw, nh); break; }
            case CropHit.Bottom when locked:
                { double nh = sh + dy, nw = nh * target; (X, Width, Height) = (centerX - nw / 2, nw, nh); break; }
            case CropHit.Left when locked:
                { double nw = sw - dx, nh = nw / target; (X, Y, Width, Height) = (sx + dx, centerY - nh / 2, nw, nh); break; }
            case CropHit.Right when locked:
                { double nw = sw + dx, nh = nw / target; (Y, Width, Height) = (centerY - nh / 2, nw, nh); break; }

            case CropHit.TopLeft: (X, Y, Width, Height) = (sx + dx, sy + dy, sw - dx, sh - dy); break;
            case CropHit.TopRight: (Y, Width, Height) = (sy + dy, sw + dx, sh - dy); break;
            case CropHit.BottomRight: (Width, Height) = (sw + dx, sh + dy); break;
            case CropHit.BottomLeft: (X, Width, Height) = (sx + dx, sw - dx, sh + dy); break;
            case CropHit.Top: (Y, Height) = (sy + dy, sh - dy); break;
            case CropHit.Bottom: Height = sh + dy; break;
            case CropHit.Left: (X, Width) = (sx + dx, sw - dx); break;
            case CropHit.Right: Width = sw + dx; break;
        }
        Clamp();
    }

    public void EndDrag() => _drag = CropHit.None;

    private void Clamp()
    {
        Width = Math.Min(1.0, Math.Max(MinDimension, Width));
        Height = Math.Min(1.0, Math.Max(MinDimension, Height));
        X = Math.Max(0, Math.Min(1 - Width, X));
        Y = Math.Max(0, Math.Min(1 - Height, Y));
    }
}
