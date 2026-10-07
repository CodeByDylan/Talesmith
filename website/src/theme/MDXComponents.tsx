import MDXComponents from '@theme-original/MDXComponents';
import Screenshot from '@site/src/components/Screenshot';
import Keys from '@site/src/components/Keys';
import Split from '@site/src/components/Split';
import Steps from '@site/src/components/Steps';
import ShortcutTable from '@site/src/components/ShortcutTable';
import Icon from '@site/src/components/Icon';
import {Card, CardGrid} from '@site/src/components/CardGrid';

/** Components available in every MDX page without an import. */
export default {
  ...MDXComponents,
  Screenshot,
  Keys,
  Split,
  Steps,
  ShortcutTable,
  Icon,
  Card,
  CardGrid,
};
