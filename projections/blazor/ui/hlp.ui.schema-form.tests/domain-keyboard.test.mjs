import assert from 'node:assert/strict';
import test from 'node:test';
import * as keyboard from '../hlp.ui.schema-form/wwwroot/schema-form.js';

test('picker navigation prevents browser submission and scrolling while ordinary text keys remain native', () => {
  // EventTarget stands in for the browser element; default prevention and listener removal are real events.
  const element = new EventTarget();
  assert.equal(typeof keyboard.bindDomainPicker, 'function');
  const binding = keyboard.bindDomainPicker(element);
  for (const [key, prevented] of [['Enter', true], ['ArrowDown', true], ['ArrowUp', true], ['Escape', true], ['a', false], ['Tab', false]]) {
    const event = new Event('keydown', { cancelable: true });
    Object.defineProperty(event, 'key', { value: key });
    element.dispatchEvent(event);
    assert.equal(event.defaultPrevented, prevented, key);
  }
  binding.dispose();
  const event = new Event('keydown', { cancelable: true });
  Object.defineProperty(event, 'key', { value: 'Enter' });
  element.dispatchEvent(event);
  assert.equal(event.defaultPrevented, false);
});
