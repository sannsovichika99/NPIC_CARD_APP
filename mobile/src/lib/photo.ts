import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import * as ImagePicker from 'expo-image-picker';

/**
 * Card photo box is 18.7 × 22 mm (≈ 17:20). Card Studio prints at 600 dpi, so ~440 × 520 px is used;
 * 1000 px wide leaves room for Remove Background… cropping on the PC while keeping uploads small (~200 KB).
 */
const ASPECT: [number, number] = [17, 20];
const MAX_WIDTH = 1000;

export type PhotoSource = 'camera' | 'library';

/** The camera is not allowed for the app: the screen shows its own (translated) message. */
export class CameraDeniedError extends Error {
  constructor() {
    super('Allow the camera for Card Studio in the phone settings to take photos.');
  }
}

/**
 * Takes (or picks) a photo, lets the user crop it to the card's photo shape, and returns a JPEG ready to upload.
 * Null when cancelled. Throws with a readable message when the camera is not allowed.
 */
export async function takeStudentPhoto(source: PhotoSource): Promise<string | null> {
  const options: ImagePicker.ImagePickerOptions = {
    mediaTypes: 'images',
    allowsEditing: true, // crop UI; the shape follows ASPECT on Android (iOS crops square, the PC crops the rest)
    aspect: ASPECT,
    quality: 1,
  };

  let result: ImagePicker.ImagePickerResult;
  if (source === 'camera') {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) throw new CameraDeniedError();
    result = await ImagePicker.launchCameraAsync({ ...options, cameraType: ImagePicker.CameraType.back });
  } else {
    result = await ImagePicker.launchImageLibraryAsync(options);
  }
  if (result.canceled || !result.assets?.length) return null;

  const asset = result.assets[0];
  const context = ImageManipulator.manipulate(asset.uri);
  if (!asset.width || asset.width > MAX_WIDTH) context.resize({ width: MAX_WIDTH, height: null });
  const image = await context.renderAsync();
  const saved = await image.saveAsync({ format: SaveFormat.JPEG, compress: 0.88 });
  return saved.uri;
}
