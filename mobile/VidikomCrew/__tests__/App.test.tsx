import React from 'react';
import ReactTestRenderer from 'react-test-renderer';
import App from '../App';
import { directorSocketService } from '../src/services/DirectorSocketService';

test('renders correctly', async () => {
  let renderer: ReactTestRenderer.ReactTestRenderer | undefined;
  await ReactTestRenderer.act(async () => {
    renderer = ReactTestRenderer.create(<App />);
  });
  await ReactTestRenderer.act(async () => {
    renderer?.unmount();
    directorSocketService.disconnect();
  });
});
