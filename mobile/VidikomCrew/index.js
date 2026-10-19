/**
 * index.js
 */

import { AppRegistry } from 'react-native';
import App from './App';

// Register for both Expo Go ('main') and bare React Native ('VidikomCrew' / 'CuePilotCrew')
AppRegistry.registerComponent('main', () => App);
AppRegistry.registerComponent('VidikomCrew', () => App);
AppRegistry.registerComponent('CuePilotCrew', () => App);

