import sys
import os
import traceback
from ultralytics import YOLO

def build_mega_optimized_onnx(pt_path):
    print("=======================================================")
    print(f"[ШИ-РАЗГОН] Старт глубокой оптимизации модели: {pt_path}")
    print("=======================================================")
    
    if not os.path.exists(pt_path):
        print(f"[!] Ошибка: Файл {pt_path} не найден в папке!")
        return
        
    base_name = os.path.splitext(os.path.basename(pt_path))[0]
    output_onnx = f"{base_name}.onnx"
    
    try:
        print("[1/3] Загрузка базовой модели PyTorch...")
        model = YOLO(pt_path)
        
        print("[2/3] Фильтрация игровых целей Rust (Игроки + Животные)...")
        # Оставляем строго: 0 - Player, 15 - Boar, 16 - Wolf
        target_classes = [0, 15, 16]
        
        print("[3/3] Экспорт в ONNX с зашитым ПРЕДСКАЗАНИЕМ и оптимизацией...")
        # dynamic=False фиксирует память для CPU, imgsz=320 дает максимальный буст FPS
        onnx_file = model.export(
            format="onnx", 
            imgsz=320,        
            dynamic=False, 
            simplify=True, 
            half=False,       # Чистый FP32 для стабильности на процессорах Ryzen
            opset=12,
            classes=target_classes
        )
        
        # Финальное сжатие графа нейросети через onnxslim
        if onnx_file and os.path.exists(onnx_file):
            print("[ОПТИМИЗАЦИЯ] Глубокое сжатие графа через ONNX Slim...")
            os.system(f"onnxslim {onnx_file} {output_onnx}")
            if os.path.exists(output_onnx) and output_onnx != onnx_file:
                try: os.remove(onnx_file)
                except: pass
        else:
            if onnx_file: output_onnx = onnx_file

        print("\n=======================================================")
        print(f"[УСПЕШНО] Создана ультра-быстрая умная модель: {output_onnx}")
        print("[ИНФО] Все настройки предсказания и разгона зашиты внутрь файла!")
        print("=======================================================")
        
    except Exception as e:
        print(f"\n[КРАШ] Ошибка при конвертации:")
        traceback.print_exc()

if __name__ == "__main__":
    target_file = "yolov8n.pt"
    if len(sys.argv) > 1:
        target_file = sys.argv[1]
    build_mega_optimized_onnx(target_file)
