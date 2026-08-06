self.onmessage = (event: MessageEvent<{ text: string; query: string }>) => {
  const query = event.data.query.trim().toLocaleLowerCase();
  if (!query) {
    self.postMessage(event.data.text.slice(0, 200_000));
    return;
  }
  const result = event.data.text
    .split(/\r?\n/)
    .filter((line) => line.toLocaleLowerCase().includes(query))
    .slice(0, 500)
    .join("\n");
  self.postMessage(result);
};

export {};
