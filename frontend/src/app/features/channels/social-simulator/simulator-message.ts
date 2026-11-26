/** Build the same customer media fixture for either local channel. */
export function simulatorMessage(mid: string, text: string, attachment: string) {
  if (!attachment) return { mid, text };
  const url =
    attachment === 'sample'
      ? 'https://media.example.test/nabra/parcel.png'
      : attachment.startsWith('data:')
        ? attachment
        : null;
  return {
    mid,
    text,
    attachments: [{ type: attachment === 'unsupported' ? 'file' : 'image', payload: { url } }],
  };
}
