# Light non-local-means denoise (stand-in for Blender's OIDN on builds without it). Copies if OpenCV is missing.
import sys, shutil
try:
    import cv2
    img = cv2.imread(sys.argv[1]); out = cv2.fastNlMeansDenoisingColored(img, None, 5, 5, 7, 21); cv2.imwrite(sys.argv[2], out)
except ImportError:
    shutil.copy(sys.argv[1], sys.argv[2])
