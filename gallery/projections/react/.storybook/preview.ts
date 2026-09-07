import type { Preview } from '@storybook/react-vite'
import '@harborline-software/ui-react/style.css'
import '../../../styles/canvas.css'

const preview: Preview = {
  parameters: {
    layout: 'centered',
    controls: { expanded: true },
    options: { storySort: { order: ['Platform', ['Button', 'Context Menu', 'Error Card', 'Loading State']] } },
    a11y: { test: 'error' },
  },
}

export default preview
