import '@testing-library/jest-dom/vitest'

const localStorageData = new Map<string, string>()
const testLocalStorage: Storage = {
  get length() { return localStorageData.size },
  clear() { localStorageData.clear() },
  getItem(key) { return localStorageData.get(key) ?? null },
  key(index) { return Array.from(localStorageData.keys())[index] ?? null },
  removeItem(key) { localStorageData.delete(key) },
  setItem(key, value) { localStorageData.set(key, String(value)) },
}
Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: testLocalStorage })
