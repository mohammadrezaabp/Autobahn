import { render } from 'solid-js/web';
import { App } from './App';
import './styles.css';
import '../../../design/autobahn.css';
import { initTheme } from '../../../design/theme';

initTheme();
render(() => <App />, document.getElementById('root')!);
