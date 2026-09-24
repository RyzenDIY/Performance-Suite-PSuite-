using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class Aimbot
{
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, uint dwExtraInfo);
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    // Змінна кістки: 0 - Голова, 1 - Шия, 2 - Тіло
    public static int aimBoneMode = 0;

    // Твій рідний лічильник кадрів затискання для No-Recoil (ПОВНІСТЮ ЗБЕРЕЖЕНО)
    public static int recoilShotCounter = 0;

    public void Run(List<BoundingBox> detections, int cx, int cy, int fov, float smooth)
    {
        if (detections.Count == 0)
        {
            recoilShotCounter = 0;
            return;
        }

        float bestDist = fov;
        BoundingBox? bestTarget = null;

        // Твій оригінальний пошук найближчої цілі до перехрестя
        foreach (var target in detections)
        {
            int dx = target.Cx - cx;
            int dy = target.Cy - cy;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = target;
            }
        }

        if (bestTarget != null)
        {
            recoilShotCounter++; // Твій лічильник кадрів росте

            // 🔄 АВТО-СИНХРОНІЗАЦІЯ: Беремо чисті глобальні координати цілі на екрані
            int targetX = bestTarget.Cx;
            int targetY = bestTarget.Cy;

            // 🎯 МІКРО-КАЛІБРУВАННЯ ХИТБОКСІВ У ПРИЦІЛІ (Точно під модельку з фото):
            if (aimBoneMode == 0)
            {
                targetY -= (int)(bestTarget.Height * 0.36f); // Ідеальний хедшот
            }
            else if (aimBoneMode == 1)
            {
                targetY -= (int)(bestTarget.Height * 0.16f); // Шия чітко під маску
            }
            else
            {
                targetY += (int)(bestTarget.Height * 0.05f); // Тіло (центр маси мішені)
            }

            // Розрахунок точної дельти відносно центру твого екрана
            int deltaX = targetX - cx;
            int deltaY = targetY - cy;

            if (Math.Abs(deltaX) < 1 && Math.Abs(deltaY) < 1) return;

            // Твій алгоритм плавної інполяції
            float speedX = deltaX / smooth;
            float speedY = deltaY / smooth;

            int moveX = speedX > 0 ? (int)Math.Ceiling(speedX) : (int)Math.Floor(speedX);
            int moveY = speedY > 0 ? (int)Math.Ceiling(speedY) : (int)Math.Floor(speedY);

            // Фізичний рух миші аїмботу
            mouse_event(MOUSEEVENTF_MOVE, moveX, moveY, 0, 0);

            // 🚀 ТВІЙ ПОРЯДОК NO-RECOIL З СИНХРОНІЗАЦІЄЮ:
            if (Program.noRecoilEnabled)
            {
                // Виклик модуля віддачі з передачею твого точного лічильника кадрів і сили
                Recoil.Compensate(recoilShotCounter, Program.recoilPower);
            }
        }
        else
        {
            recoilShotCounter = 0;
        }
    }
}
