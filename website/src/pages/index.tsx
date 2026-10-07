import type {ReactElement} from 'react';
import Layout from '@theme/Layout';
import Hero from '@site/src/components/home/Hero';
import Areas from '@site/src/components/home/Areas';
import Showcase from '@site/src/components/home/Showcase';
import Workflow from '@site/src/components/home/Workflow';
import Highlights from '@site/src/components/home/Highlights';
import CallToAction from '@site/src/components/home/CallToAction';

export default function Home(): ReactElement {
  return (
    <Layout
      title="2D game engine and editor for .NET"
      description="Documentation for Talesmith, a 2D game engine and editor for .NET 10: the editor guide, C# scripting, plugins and how the engine is built.">
      <main>
        <Hero />
        <Areas />
        <Showcase />
        <Workflow />
        <Highlights />
        <CallToAction />
      </main>
    </Layout>
  );
}
