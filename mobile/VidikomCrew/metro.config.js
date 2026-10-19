const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);
config.serializer = config.serializer || {};
config.serializer.getPolyfills = () => require('@react-native/js-polyfills')();

module.exports = config;
